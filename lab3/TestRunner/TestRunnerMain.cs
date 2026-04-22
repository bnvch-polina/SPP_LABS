namespace SPP_LAB_1_TRIAL;

public class TestRunnerMain
{
    public static async Task Main(string[] args)
    {
        var assemblyPath = GetArgument(args, "--assembly")
            ?? (args.Length > 0 && !args[0].StartsWith("--") ? args[0] : ResolveDefaultTestsAssemblyPath());
        var mode = (GetArgument(args, "--mode") ?? "parallel").ToLowerInvariant();
        var compare = mode == "compare";
        var dynamic = mode == "dynamic";
        var maxDegreeValue = GetArgument(args, "--maxdop");
        var maxDegree = int.TryParse(maxDegreeValue, out var parsed) ? Math.Max(1, parsed) : Environment.ProcessorCount;
        var outputPath = GetArgument(args, "--out");
        var repetitions = ParseInt(GetArgument(args, "--repetitions"), defaultValue: dynamic ? 2 : 1, min: 1);
        var minThreads = ParseInt(GetArgument(args, "--minthreads"), defaultValue: 2, min: 1);
        var maxThreads = ParseInt(GetArgument(args, "--maxthreads"), defaultValue: Math.Max(4, Environment.ProcessorCount * 2), min: minThreads);
        var idleTimeoutMs = ParseInt(GetArgument(args, "--idlems"), defaultValue: 1200, min: 50);
        var queueWaitMs = ParseInt(GetArgument(args, "--queuewaitms"), defaultValue: 500, min: 10);
        var workerHangMs = ParseInt(GetArgument(args, "--hangms"), defaultValue: 8000, min: 100);
        var monitorMs = ParseInt(GetArgument(args, "--monitorms"), defaultValue: 300, min: 50);
        var simulateLoad = ParseBool(GetArgument(args, "--simulateload"), defaultValue: dynamic);

        var executor = new TestExecutor();
        if (compare)
        {
            var sequentialSummary = await executor.RunAsync(assemblyPath, new TestExecutor.TestRunOptions
            {
                RunParallel = false,
                MaxDegreeOfParallelism = 1,
                OutputFilePath = outputPath
            });
            PrintSummary("SEQUENTIAL", sequentialSummary);

            var parallelSummary = await executor.RunAsync(assemblyPath, new TestExecutor.TestRunOptions
            {
                RunParallel = true,
                MaxDegreeOfParallelism = maxDegree,
                OutputFilePath = outputPath
            });
            PrintSummary($"PARALLEL (MaxDegree={maxDegree})", parallelSummary);

            var speedup = sequentialSummary.Elapsed.TotalMilliseconds / Math.Max(1.0, parallelSummary.Elapsed.TotalMilliseconds);
            Console.WriteLine($"\nSpeedup: {speedup:F2}x");
            Environment.ExitCode = parallelSummary.Failed == 0 ? 0 : 1;
            return;
        }

        var summary = await executor.RunAsync(assemblyPath, new TestExecutor.TestRunOptions
        {
            RunParallel = mode != "sequential",
            MaxDegreeOfParallelism = maxDegree,
            OutputFilePath = outputPath,
            UseDynamicPool = dynamic,
            Repetitions = repetitions,
            SimulateUnevenLoad = simulateLoad,
            MinThreads = minThreads,
            MaxThreads = maxThreads,
            IdleTimeoutMs = idleTimeoutMs,
            QueueWaitThresholdMs = queueWaitMs,
            WorkerHangTimeoutMs = workerHangMs,
            MonitorIntervalMs = monitorMs
        });

        PrintSummary(mode.ToUpperInvariant(), summary);
        Environment.ExitCode = summary.Failed == 0 ? 0 : 1;
    }

    private static void PrintSummary(string mode, TestExecutor.TestRunSummary summary)
    {
        Console.WriteLine($"\n=== SUMMARY [{mode}] ===");
        Console.WriteLine($"Total:   {summary.Total}");
        Console.WriteLine($"Passed:  {summary.Passed}");
        Console.WriteLine($"Failed:  {summary.Failed}");
        Console.WriteLine($"Elapsed: {summary.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static string? GetArgument(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ParseInt(string? value, int defaultValue, int min)
    {
        if (int.TryParse(value, out var parsed))
        {
            return Math.Max(min, parsed);
        }

        return defaultValue;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static string ResolveDefaultTestsAssemblyPath()
    {
        var directPath = Path.Combine(AppContext.BaseDirectory, "TestsForProject.dll");
        if (File.Exists(directPath))
        {
            return directPath;
        }

        var current = Directory.GetCurrentDirectory();
        var candidate = Path.GetFullPath(Path.Combine(current, "TestsForProject", "bin", "Debug", "net9.0", "TestsForProject.dll"));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return directPath;
    }
}