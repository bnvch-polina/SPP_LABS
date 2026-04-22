using System.Reflection;
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
    }

    public async Task<TestRunSummary> RunAsync(string assemblyPath, TestRunOptions? options = null)
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

        var testsToRun = BuildExecutionPlan(testClasses).ToList();
        WriteLineSafe(writer, $"\n=== RUN MODE: {(options.RunParallel ? "PARALLEL" : "SEQUENTIAL")} | MaxDegreeOfParallelism={maxDegree} ===");

        if (options.RunParallel)
        {
            await Parallel.ForEachAsync(
                testsToRun,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
                async (scheduledTest, _) =>
                {
                    var result = await RunSingleMethodAsync(
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
                });
        }
        else
        {
            foreach (var scheduledTest in testsToRun)
            {
                var result = await RunSingleMethodAsync(
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
        return summary;
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
            var testMethods = testClass.GetMethods()
                .Where(m => m.GetCustomAttribute<Atributes.TestMethodAttribute>() != null);

            foreach (var testMethod in testMethods)
            {
                var methodMeta = testMethod.GetCustomAttribute<Atributes.TestMethodAttribute>();
                var description = methodMeta?.Description ?? testMethod.Name;

                var testCases = testMethod.GetCustomAttributes<Atributes.TestCaseAttribute>().ToList();
                if (testCases.Count == 0)
                {
                    yield return new ScheduledTest
                    {
                        TestClass = testClass,
                        TestMethod = testMethod,
                        Description = description,
                        CaseIndex = 1,
                        Parameters = null
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
                            Parameters = testCase.Parameters
                        };
                        index++;
                    }
                }
            }
        }
    }

    private static async Task<TestExecutionResult> RunSingleMethodAsync(
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
                await InvokeWithPossibleTaskAsync(instance, setupMethod, null);
            }

            var convertedParams = BuildCallParameters(testMethod, parameters);
            await InvokeWithTimeoutAsync(instance, testMethod, convertedParams, timeoutMs);

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
                await InvokeWithPossibleTaskAsync(instance, tearDownMethod, null);
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

    private static async Task InvokeWithPossibleTaskAsync(object instance, MethodInfo method, object[]? parameters)
    {
        var result = method.Invoke(instance, parameters);
        if (result is Task task)
        {
            await task;
        }
    }

    private static async Task InvokeWithTimeoutAsync(object instance, MethodInfo method, object[]? parameters, int timeoutMs)
    {
        var invocationTask = Task.Run(async () => await InvokeWithPossibleTaskAsync(instance, method, parameters));
        var timeoutTask = Task.Delay(timeoutMs);
        var completedTask = await Task.WhenAny(invocationTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            throw new TimeoutException($"Execution exceeded timeout ({timeoutMs} ms).");
        }

        await invocationTask;
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