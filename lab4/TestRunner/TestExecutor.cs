using System.Diagnostics;
using System.Reflection;
using System.Threading;
using TestingFramework;
using TestingFramework.Exeptions;

namespace SPP_LAB_1_TRIAL;

public class TestExecutor
{
    private const int DefaultTimeoutMs = 5000;
    private readonly object _outputSync = new();
    private readonly object _summarySync = new();

    public sealed class TestRunOptions
    {
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
        public bool RunParallel { get; init; } = true;
        public string? OutputFilePath { get; init; }
        public bool UseDynamicPool { get; init; }
        public int Repetitions { get; init; } = 1;
        public bool SimulateUnevenLoad { get; init; } = false;
        public int MinThreads { get; init; } = 2;
        public int MaxThreads { get; init; } = Math.Max(4, Environment.ProcessorCount * 2);
        public int IdleTimeoutMs { get; init; } = 1200;
        public int QueueWaitThresholdMs { get; init; } = 500;
        public int WorkerHangTimeoutMs { get; init; } = 8000;
        public int MonitorIntervalMs { get; init; } = 300;

        // Фильтрация тестов по атрибутным метаданным.
        public string? FilterCategory { get; init; }
        public string? FilterAuthor { get; init; }
        public int? MinPriority { get; init; }
    }

    public sealed class TestExecutionResult
    {
        public string ClassName { get; init; } = string.Empty;
        public string MethodName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public int CaseIndex { get; init; }
        public bool IsPassed { get; init; }
        public string? ErrorMessage { get; init; }
        public bool IsTimedOut { get; init; }
        public TimeSpan Duration { get; init; }
        public string ThreadInfo { get; init; } = string.Empty;
    }

    public sealed class TestRunSummary
    {
        public List<TestExecutionResult> Results { get; } = new();
        public int Total => Results.Count;
        public int Passed => Results.Count(r => r.IsPassed);
        public int Failed => Total - Passed;
        public TimeSpan Elapsed { get; set; }
    }

    private sealed class ScheduledTest
    {
        public Type TestClass { get; init; } = null!;
        public MethodInfo TestMethod { get; init; } = null!;
        public string Description { get; init; } = string.Empty;
        public int CaseIndex { get; init; }
        public object[]? Parameters { get; init; }

        public string? Category { get; init; }
        public int Priority { get; init; }
        public string? Author { get; init; }
    }

    public Task<TestRunSummary> RunAsync(string assemblyPath, TestRunOptions? options = null)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Tests assembly not found: {assemblyPath}");
        }

        options ??= new TestRunOptions();
        var maxDegree = Math.Max(1, options.MaxDegreeOfParallelism);

        var summary = new TestRunSummary();
        var assembly = Assembly.LoadFrom(assemblyPath);
        SharedContextStore.Clear();
        var startedAt = DateTime.UtcNow;
        using var writer = CreateWriter(options.OutputFilePath);

        var testClasses = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<Atributes.TestClassAttribute>() != null);

        var allTestsPlanned = BuildExecutionPlan(testClasses).ToList();
        var filter = BuildFilter(options);
        var testsToRun = allTestsPlanned.Where(filter).ToList();

        var repetitions = Math.Max(1, options.Repetitions);
        var executionPlan = ExpandPlan(testsToRun, repetitions).ToList();
        var totalLaunches = executionPlan.Count;
        var modeText = options.UseDynamicPool
            ? "DYNAMIC-POOL"
            : options.RunParallel ? "PARALLEL" : "SEQUENTIAL";

        WriteLineSafe(writer,
            $"\n=== RUN MODE: {modeText} | MaxDegree={maxDegree} | Planned launches={totalLaunches} ===");
        if (!string.IsNullOrWhiteSpace(options.FilterCategory) || !string.IsNullOrWhiteSpace(options.FilterAuthor) || options.MinPriority.HasValue)
        {
            WriteLineSafe(writer, $"Filter: category='{options.FilterCategory ?? "*"}', author='{options.FilterAuthor ?? "*"}', minPriority='{options.MinPriority?.ToString() ?? "*"}'");
            WriteLineSafe(writer, $"Discovered: {allTestsPlanned.Count} | After filter: {testsToRun.Count} | Repetitions: {repetitions}");
        }
        if (options.UseDynamicPool && totalLaunches < 50)
        {
            WriteLineSafe(writer, $"WARNING: launches={totalLaunches}. For lab requirement use >= 50.");
        }

        if (options.UseDynamicPool)
        {
            RunWithDynamicPool(executionPlan, summary, options, writer);
        }
        else
        {
            foreach (var scheduledTest in executionPlan)
            {
                var result = RunSingleMethod(
                    scheduledTest.TestClass,
                    scheduledTest.TestMethod,
                    scheduledTest.Description,
                    scheduledTest.CaseIndex,
                    scheduledTest.Parameters);
                summary.Results.Add(result);
                PrintCaseResult(result, writer);
            }
        }

        summary.Elapsed = DateTime.UtcNow - startedAt;
        WriteLineSafe(writer, $"Elapsed: {summary.Elapsed.TotalMilliseconds:F0} ms");
        return Task.FromResult(summary);
    }
    private IEnumerable<ScheduledTest> ExpandPlan(IReadOnlyList<ScheduledTest> sourcePlan, int repetitions)
    {
        for (int i = 0; i < repetitions; i++)
        {
            foreach (var test in sourcePlan)
            {
                yield return new ScheduledTest
                {
                    TestClass = test.TestClass,
                    TestMethod = test.TestMethod,
                    Description = test.Description + (repetitions > 1 ? $" [run {i + 1}]" : string.Empty),
                    CaseIndex = test.CaseIndex,
                        Parameters = test.Parameters,
                        Category = test.Category,
                        Priority = test.Priority,
                        Author = test.Author
                };
            }
        }
    }

    private static Func<ScheduledTest, bool> BuildFilter(TestRunOptions options)
    {
        return scheduled =>
        {
            if (!string.IsNullOrWhiteSpace(options.FilterCategory))
            {
                if (!string.Equals(scheduled.Category, options.FilterCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(options.FilterAuthor))
            {
                if (!string.Equals(scheduled.Author, options.FilterAuthor, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (options.MinPriority.HasValue)
            {
                if (scheduled.Priority < options.MinPriority.Value)
                {
                    return false;
                }
            }

            return true;
        };
    }

    //тут запуск через пул
    private void RunWithDynamicPool(
        IReadOnlyList<ScheduledTest> executionPlan,
        TestRunSummary summary,
        TestRunOptions options,
        StreamWriter? writer)
    {
        using var waitHandle = new CountdownEvent(executionPlan.Count);
        using var pool = new DynamicThreadPool(new DynamicThreadPool.PoolOptions
        {
            MinThreads = Math.Max(1, options.MinThreads),
            MaxThreads = Math.Max(Math.Max(1, options.MinThreads), options.MaxThreads),
            IdleTimeout = TimeSpan.FromMilliseconds(Math.Max(50, options.IdleTimeoutMs)),
            QueueWaitThreshold = TimeSpan.FromMilliseconds(Math.Max(10, options.QueueWaitThresholdMs)),
            WorkerHangTimeout = TimeSpan.FromMilliseconds(Math.Max(100, options.WorkerHangTimeoutMs)),
            MonitorInterval = TimeSpan.FromMilliseconds(Math.Max(50, options.MonitorIntervalMs))
        });

        var printedStats = Stopwatch.StartNew();
        pool.OnLog += msg => WriteLineSafe(writer, msg);
        pool.OnStatsChanged += stats =>
        {
            if (printedStats.ElapsedMilliseconds < 500)
            {
                return;
            }

            printedStats.Restart();
            WriteLineSafe(writer, $"[monitor] queue={stats.QueueLength}, workers={stats.ActiveWorkers}, busy={stats.BusyWorkers}, done={stats.CompletedTasks}, failed={stats.FailedTasks}, recovered={stats.RecoveredHungWorkers}");
        };

        pool.OnPoolStarted += () => WriteLineSafe(writer, "[event] pool started");
        pool.OnPoolShutdown += () => WriteLineSafe(writer, "[event] pool shutdown requested");
        pool.OnWorkerSpawned += (workerId, reason) => WriteLineSafe(writer, $"[event] worker spawned #{workerId}: {reason}");
        pool.OnWorkerRetired += (workerId, reason) => WriteLineSafe(writer, $"[event] worker retired #{workerId}: {reason}");

        int enqueuedLogged = 0;
        int startedLogged = 0;
        int completedLogged = 0;

        pool.OnTaskEnqueued += desc =>
        {
            if (enqueuedLogged++ < 10)
            {
                WriteLineSafe(writer, $"[event] task enqueued: {desc}");
            }
        };

        pool.OnTaskStarted += (workerId, desc) =>
        {
            if (startedLogged++ < 10)
            {
                WriteLineSafe(writer, $"[event] task started on worker #{workerId}: {desc}");
            }
        };

        pool.OnTaskCompleted += (workerId, desc) =>
        {
            if (completedLogged++ < 10)
            {
                WriteLineSafe(writer, $"[event] task completed on worker #{workerId}: {desc}");
            }
        };

        pool.OnTaskFailed += (workerId, desc, error) =>
        {
            WriteLineSafe(writer, $"[event] task failed on worker #{workerId}: {desc}. Error: {error}");
        };

        var producer = options.SimulateUnevenLoad
            ? BuildUnevenLoadDelays(executionPlan.Count)
            : Enumerable.Repeat(0, executionPlan.Count);

        int index = 0;
        foreach (var scheduledTest in executionPlan)
        {
            var delay = producer.ElementAt(index++);
            if (delay > 0)
            {
                Thread.Sleep(delay);
            }

            pool.Enqueue(() =>
            {
                try
                {
                    var result = RunSingleMethod(
                        scheduledTest.TestClass,
                        scheduledTest.TestMethod,
                        scheduledTest.Description,
                        scheduledTest.CaseIndex,
                        scheduledTest.Parameters);

                    lock (_summarySync)
                    {
                        summary.Results.Add(result);
                    }

                    PrintCaseResult(result, writer);
                }
                catch (Exception ex)
                {
                    var fallback = new TestExecutionResult
                    {
                        ClassName = scheduledTest.TestClass.Name,
                        MethodName = scheduledTest.TestMethod.Name,
                        Description = scheduledTest.Description,
                        CaseIndex = scheduledTest.CaseIndex,
                        IsPassed = false,
                        ErrorMessage = $"ExecutorError: {ex.Message}",
                        Duration = TimeSpan.Zero,
                        ThreadInfo = $"Thread #{Environment.CurrentManagedThreadId}"
                    };

                    lock (_summarySync)
                    {
                        summary.Results.Add(fallback);
                    }
                    PrintCaseResult(fallback, writer);
                }
                finally
                {
                    waitHandle.Signal();
                }
            }, $"{scheduledTest.TestClass.Name}.{scheduledTest.TestMethod.Name}");
        }

        waitHandle.Wait();
    }

    private IEnumerable<int> BuildUnevenLoadDelays(int count)
    {
        if (count == 0)
        {
            yield break;
        }

        var random = new Random(42);
        for (int i = 0; i < count; i++)
        {
            if (i % 17 == 0)
            {
                yield return 700;
            }
            else if (i % 9 == 0)
            {
                yield return 300;
            }
            else if (i % 5 == 0)
            {
                yield return 0;
            }
            else
            {
                yield return random.Next(20, 120);
            }
        }
    }


    private static StreamWriter? CreateWriter(string? outputFilePath)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(outputFilePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return new StreamWriter(fullPath, append: false) { AutoFlush = true };
    }

    private IEnumerable<ScheduledTest> BuildExecutionPlan(IEnumerable<Type> testClasses)
    {
        foreach (var testClass in testClasses)
        {
            var classAttr = testClass.GetCustomAttribute<Atributes.TestClassAttribute>();
            var classCategory = classAttr?.Category;

            var testMethods = testClass.GetMethods()
                .Where(m => m.GetCustomAttribute<Atributes.TestMethodAttribute>() != null);

            foreach (var testMethod in testMethods)
            {
                var methodMeta = testMethod.GetCustomAttribute<Atributes.TestMethodAttribute>();
                var description = methodMeta?.Description ?? testMethod.Name;

                var meta = testMethod.GetCustomAttribute<Atributes.TestMetaAttribute>();
                var category = meta?.Category ?? classCategory;
                var priority = meta?.Priority ?? 0;
                var author = meta?.Author;

                var sourceAttr = testMethod.GetCustomAttribute<Atributes.TestCaseSourceAttribute>();
                if (sourceAttr != null)
                {
                    var sourceMethod = testClass
                        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == sourceAttr.SourceMemberName && m.GetParameters().Length == 0);

                    if (sourceMethod == null)
                    {
                        throw new InvalidOperationException(
                            $"TestCaseSource member '{sourceAttr.SourceMemberName}' was not found in {testClass.FullName}.");
                    }

                    var sourceInstance = sourceMethod.IsStatic ? null : Activator.CreateInstance(testClass);
                    var rawCases = sourceMethod.Invoke(sourceInstance, null);

                    if (rawCases is System.Collections.IEnumerable enumerable)
                    {
                        int index = 1;
                        foreach (var rawCase in enumerable)
                        {
                            if (rawCase is not object[] parameters)
                            {
                                throw new InvalidOperationException(
                                    $"TestCaseSource '{sourceAttr.SourceMemberName}' must yield object[] parameters.");
                            }

                            yield return new ScheduledTest
                            {
                                TestClass = testClass,
                                TestMethod = testMethod,
                                Description = description,
                                CaseIndex = index,
                                Parameters = parameters,
                                Category = category,
                                Priority = priority,
                                Author = author
                            };
                            index++;
                        }

                        // sourceAttr присутствует — TestCaseAttribute игнорируем.
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"TestCaseSource '{sourceAttr.SourceMemberName}' must return IEnumerable<object[]> (or compatible IEnumerable).");
                }

                var testCases = testMethod.GetCustomAttributes<Atributes.TestCaseAttribute>().ToList();
                if (testCases.Count == 0)
                {
                    yield return new ScheduledTest
                    {
                        TestClass = testClass,
                        TestMethod = testMethod,
                        Description = description,
                        CaseIndex = 1,
                        Parameters = null,
                        Category = category,
                        Priority = priority,
                        Author = author
                    };
                }
                else
                {
                    int index = 1;
                    foreach (var testCase in testCases)
                    {
                        yield return new ScheduledTest
                        {
                            TestClass = testClass,
                            TestMethod = testMethod,
                            Description = description,
                            CaseIndex = index,
                            Parameters = testCase.Parameters,
                            Category = category,
                            Priority = priority,
                            Author = author
                        };
                        index++;
                    }
                }
            }
        }
    }

    private static TestExecutionResult RunSingleMethod(
        Type testClass,
        MethodInfo testMethod,
        string description,
        int caseIndex,
        object[]? parameters)
    {
        var instance = Activator.CreateInstance(testClass) ??
                       throw new InvalidOperationException($"Cannot create test class instance: {testClass.Name}");
        var timeoutMs = testMethod.GetCustomAttribute<Atributes.TimeoutAttribute>()?.Milliseconds ?? DefaultTimeoutMs;
        var startedAt = DateTime.UtcNow;
        var threadInfo = $"Thread #{Environment.CurrentManagedThreadId}";

        try
        {
            var setupMethod = testClass.GetMethods()
                .FirstOrDefault(m => m.GetCustomAttribute<Atributes.SetupAttribute>() != null);
            if (setupMethod != null)
            {
                InvokeWithPossibleTask(instance, setupMethod, null, timeoutMs);
            }

            var convertedParams = BuildCallParameters(testMethod, parameters);
            InvokeWithPossibleTask(instance, testMethod, convertedParams, timeoutMs);

            return new TestExecutionResult
            {
                ClassName = testClass.Name,
                MethodName = testMethod.Name,
                Description = description,
                CaseIndex = caseIndex,
                IsPassed = true,
                Duration = DateTime.UtcNow - startedAt,
                ThreadInfo = threadInfo
            };
        }
        catch (TimeoutException ex)
        {
            return new TestExecutionResult
            {
                ClassName = testClass.Name,
                MethodName = testMethod.Name,
                Description = description,
                CaseIndex = caseIndex,
                IsPassed = false,
                IsTimedOut = true,
                ErrorMessage = ex.Message,
                Duration = DateTime.UtcNow - startedAt,
                ThreadInfo = threadInfo
            };
        }
        catch (TargetInvocationException ex)
        {
            return BuildFailedResult(testClass, testMethod, description, caseIndex, ex.InnerException ?? ex, startedAt, threadInfo);
        }
        catch (Exception ex)
        {
            return BuildFailedResult(testClass, testMethod, description, caseIndex, ex, startedAt, threadInfo);
        }
        finally
        {
            var tearDownMethod = testClass.GetMethods()
                .FirstOrDefault(m => m.GetCustomAttribute<Atributes.TearDownAttribute>() != null);
            if (tearDownMethod != null)
            {
                InvokeWithPossibleTask(instance, tearDownMethod, null, timeoutMs);
            }
        }
    }

    private static TestExecutionResult BuildFailedResult(
        Type testClass,
        MethodInfo testMethod,
        string description,
        int caseIndex,
        Exception ex,
        DateTime startedAt,
        string threadInfo)
    {
        var message = ex is AssertExpression ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
        return new TestExecutionResult
        {
            ClassName = testClass.Name,
            MethodName = testMethod.Name,
            Description = description,
            CaseIndex = caseIndex,
            IsPassed = false,
            ErrorMessage = message,
            Duration = DateTime.UtcNow - startedAt,
            ThreadInfo = threadInfo
        };
    }

    private void PrintCaseResult(TestExecutionResult result, StreamWriter? writer)
    {
        var statusText = result.IsPassed
            ? "PASSED"
            : result.IsTimedOut
                ? $"TIMEOUT: {result.ErrorMessage}"
                : $"FAILED: {result.ErrorMessage}";

        var line = $"[{result.ClassName}.{result.MethodName} case #{result.CaseIndex}] {statusText} | {result.Duration.TotalMilliseconds:F0} ms | {result.ThreadInfo}";

        lock (_outputSync)
        {
            Console.ForegroundColor = result.IsPassed ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(line);
            Console.ResetColor();
            writer?.WriteLine(line);
        }
    }

    private void WriteLineSafe(StreamWriter? writer, string line)
    {
        lock (_outputSync)
        {
            Console.WriteLine(line);
            writer?.WriteLine(line);
        }
    }

    private static object[] BuildCallParameters(MethodInfo method, object[]? rawParameters)
    {
        var methodParams = method.GetParameters();
        var callParams = new object[methodParams.Length];

        for (int i = 0; i < methodParams.Length; i++)
        {
            var targetType = methodParams[i].ParameterType;
            if (rawParameters != null && i < rawParameters.Length)
            {
                callParams[i] = ConvertParameter(rawParameters[i], targetType);
            }
            else
            {
                callParams[i] = GetDefaultValue(targetType);
            }
        }

        return callParams;
    }

    private static void InvokeWithPossibleTask(object instance, MethodInfo method, object[]? parameters, int timeoutMs)
    {
        var result = method.Invoke(instance, parameters);
        if (result is Task task)
        {
            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException($"Execution exceeded timeout ({timeoutMs} ms).");
            }
        }
    }

    private static object ConvertParameter(object value, Type targetType)
    {
        if (value == null)
        {
            return GetDefaultValue(targetType);
        }

        if (value.GetType() == targetType)
        {
            return value;
        }

        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                return ConvertParameter(value, underlyingType);
            }
        }

        if (targetType == typeof(DateTime) && value is string stringValue)
        {
            return DateTime.Parse(stringValue);
        }

        return Convert.ChangeType(value, targetType);
    }

    private static object GetDefaultValue(Type type)
    {
        if (!type.IsValueType)
        {
            return null!;
        }

        return Activator.CreateInstance(type)!;
    }
}