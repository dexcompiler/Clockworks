using System.Text;
using System.Text.Json;

namespace Clockworks.Demo.Workloads;

public static class WorkloadCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyList<IWorkloadScenario> Scenarios =
    [
        new OrderPipelineWorkloadScenario(),
        new TimerTimeoutStormWorkloadScenario(),
        new UuidHlcHotPathWorkloadScenario(),
        new CausalFanoutFanInWorkloadScenario(),
        new LongRunningSoakWorkloadScenario()
    ];

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return;
        }

        var selectedNames = ParseScenarioNames(args);
        var runtimeMode = ParseRuntimeMode(GetOption(args, "--mode") ?? "simulated");
        var seed = ParseInt(GetOption(args, "--seed"), 12345);
        var outputDirectory = GetOption(args, "--output")
            ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "workloads");
        var baselinePath = GetOption(args, "--baseline");

        Directory.CreateDirectory(outputDirectory);

        var baselines = LoadBaselines(baselinePath);
        var context = new WorkloadExecutionContext(runtimeMode, seed, outputDirectory, baselines);

        var scenarios = ResolveScenarios(selectedNames);
        var startedUtc = DateTimeOffset.UtcNow;
        var results = new List<WorkloadResult>(scenarios.Count);

        foreach (var scenario in scenarios)
        {
            results.Add(await ExecuteScenarioAsync(scenario, context, cancellationToken));
        }

        var report = new WorkloadRunReport(
            startedUtc,
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            results);

        await WriteArtifactsAsync(report, outputDirectory, cancellationToken);

        Console.WriteLine(BuildConsoleSummary(report));

        if (report.HasFailures)
        {
            throw new InvalidOperationException($"One or more workload scenarios reported failures or regressions. See '{outputDirectory}'.");
        }
    }

    public static IReadOnlyList<IWorkloadScenario> GetScenarios() => Scenarios;

    private static async Task<WorkloadResult> ExecuteScenarioAsync(
        IWorkloadScenario scenario,
        WorkloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!SupportsMode(scenario.SupportedModes, context.RuntimeMode))
        {
            return new WorkloadResult(
                scenario.Name,
                context.RuntimeMode,
                context.Seed,
                WorkloadStatus.Skipped,
                $"Scenario does not support runtime mode '{context.RuntimeMode}'.",
                0,
                [],
                [],
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                BuildReproCommand(scenario.Name, context));
        }

        return await scenario.ExecuteAsync(context, cancellationToken);
    }

    private static bool SupportsMode(WorkloadRuntimeMode supported, WorkloadRuntimeMode requested) =>
        requested == WorkloadRuntimeMode.Both ||
        supported == WorkloadRuntimeMode.Both ||
        supported == requested;

    private static IReadOnlyList<IWorkloadScenario> ResolveScenarios(IReadOnlyList<string> names)
    {
        if (names.Count == 0 || names.Contains("all", StringComparer.OrdinalIgnoreCase))
        {
            return Scenarios;
        }

        var selected = new List<IWorkloadScenario>(names.Count);
        foreach (var name in names)
        {
            var scenario = Scenarios.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (scenario is null)
            {
                throw new ArgumentException($"Unknown workload scenario '{name}'.");
            }

            selected.Add(scenario);
        }

        return selected;
    }

    private static IReadOnlyList<string> ParseScenarioNames(string[] args)
    {
        var names = new List<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }

            names.Add(arg);
        }

        return names;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }

            if (args[i].StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static WorkloadRuntimeMode ParseRuntimeMode(string value) =>
        Enum.TryParse<WorkloadRuntimeMode>(value, true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unsupported runtime mode '{value}'.");

    private static WorkloadBaselineSet LoadBaselines(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return WorkloadBaselineSet.Empty;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkloadBaselineSet>(json, JsonOptions) ?? WorkloadBaselineSet.Empty;
    }

    private static async Task WriteArtifactsAsync(
        WorkloadRunReport report,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "workload-report.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);

        foreach (var result in report.Results)
        {
            var scenarioDirectory = Path.Combine(outputDirectory, result.Scenario);
            Directory.CreateDirectory(scenarioDirectory);

            await File.WriteAllTextAsync(
                Path.Combine(scenarioDirectory, "result.json"),
                JsonSerializer.Serialize(result, JsonOptions),
                cancellationToken);
        }

        var summaryPath = Path.Combine(outputDirectory, "summary.md");
        await File.WriteAllTextAsync(summaryPath, BuildMarkdownSummary(report), cancellationToken);

        var issuesDirectory = Path.Combine(outputDirectory, "issues");
        Directory.CreateDirectory(issuesDirectory);

        foreach (var result in report.Results.Where(r => r.Status is WorkloadStatus.Failed or WorkloadStatus.Regressed))
        {
            var fileName = $"{SanitizeFileName(result.Scenario)}-{result.Status.ToString().ToLowerInvariant()}.md";
            await File.WriteAllTextAsync(
                Path.Combine(issuesDirectory, fileName),
                BuildIssueMarkdown(result),
                cancellationToken);
        }
    }

    private static string BuildConsoleSummary(WorkloadRunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Clockworks workload summary");
        builder.AppendLine(new string('=', 28));

        foreach (var result in report.Results)
        {
            builder.AppendLine($"{result.Scenario}: {result.Status} ({result.DurationMs:F1} ms) - {result.Summary}");
        }

        return builder.ToString();
    }

    private static string BuildMarkdownSummary(WorkloadRunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Clockworks workload summary");
        builder.AppendLine();
        builder.AppendLine($"- Started: {report.StartedUtc:O}");
        builder.AppendLine($"- Finished: {report.FinishedUtc:O}");
        builder.AppendLine($"- Host: {report.Host}");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Status | Duration (ms) | Summary |");
        builder.AppendLine("| --- | --- | ---: | --- |");

        foreach (var result in report.Results)
        {
            builder.AppendLine($"| {result.Scenario} | {result.Status} | {result.DurationMs:F1} | {result.Summary} |");
        }

        return builder.ToString();
    }

    private static string BuildIssueMarkdown(WorkloadResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# [Workload Signal] {result.Scenario} {result.Status.ToString().ToLowerInvariant()}");
        builder.AppendLine();
        builder.AppendLine($"- Runtime mode: {result.RuntimeMode}");
        builder.AppendLine($"- Seed: {result.Seed}");
        builder.AppendLine($"- Duration: {result.DurationMs:F1} ms");
        builder.AppendLine($"- Summary: {result.Summary}");
        builder.AppendLine();
        builder.AppendLine("## Findings");
        builder.AppendLine();

        foreach (var finding in result.Findings)
        {
            builder.AppendLine($"- **{finding.Category} / {finding.Title}:** {finding.Details}");
        }

        builder.AppendLine();
        builder.AppendLine("## Reproduction");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine(result.ReproductionCommand);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Metrics");
        builder.AppendLine();

        foreach (var metric in result.Metrics.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- `{metric.Name}` = {metric.Value:F2} {metric.Unit}");
        }

        builder.AppendLine();
        builder.AppendLine("## Invariants");
        builder.AppendLine();

        foreach (var invariant in result.Invariants)
        {
            builder.AppendLine($"- [{(invariant.Passed ? "x" : " ")}] **{invariant.Name}** — {invariant.Details}");
        }

        return builder.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }

    public static WorkloadResult FinalizeResult(
        string scenario,
        WorkloadRuntimeMode runtimeMode,
        int seed,
        string summary,
        TimeSpan duration,
        IEnumerable<WorkloadMetric> metrics,
        IEnumerable<WorkloadInvariant> invariants,
        WorkloadBaselineSet baselineSet,
        DateTimeOffset startedUtc,
        DateTimeOffset finishedUtc,
        string reproductionCommand)
    {
        var metricList = metrics.ToList();
        var invariantList = invariants.ToList();
        var findings = new List<WorkloadFinding>();

        foreach (var invariant in invariantList.Where(i => !i.Passed))
        {
            findings.Add(new WorkloadFinding("Invariant", invariant.Name, invariant.Details));
        }

        if (baselineSet.Scenarios.TryGetValue(scenario, out var scenarioBaseline))
        {
            foreach (var metric in metricList)
            {
                if (!scenarioBaseline.Metrics.TryGetValue(metric.Name, out var baseline))
                {
                    continue;
                }

                AddBaselineFindings(findings, metric, baseline);
            }
        }

        var status = findings.Any(f => string.Equals(f.Category, "Invariant", StringComparison.Ordinal))
            ? WorkloadStatus.Failed
            : findings.Count > 0
                ? WorkloadStatus.Regressed
                : WorkloadStatus.Passed;

        return new WorkloadResult(
            scenario,
            runtimeMode,
            seed,
            status,
            summary,
            duration.TotalMilliseconds,
            metricList,
            invariantList,
            findings,
            startedUtc,
            finishedUtc,
            reproductionCommand);
    }

    private static void AddBaselineFindings(
        ICollection<WorkloadFinding> findings,
        WorkloadMetric metric,
        WorkloadMetricBaseline baseline)
    {
        if (baseline.Min.HasValue && metric.Value < baseline.Min.Value)
        {
            findings.Add(new WorkloadFinding(
                "Baseline",
                metric.Name,
                $"{metric.Value:F2}{metric.Unit} is below minimum {baseline.Min.Value:F2}{metric.Unit}."));
        }

        if (baseline.Max.HasValue && metric.Value > baseline.Max.Value)
        {
            findings.Add(new WorkloadFinding(
                "Baseline",
                metric.Name,
                $"{metric.Value:F2}{metric.Unit} is above maximum {baseline.Max.Value:F2}{metric.Unit}."));
        }

        if (!baseline.Baseline.HasValue || !baseline.MaxRegressionPercent.HasValue)
        {
            return;
        }

        var allowed = baseline.MaxRegressionPercent.Value / 100d;
        if (baseline.HigherIsBetter)
        {
            var floor = baseline.Baseline.Value * (1d - allowed);
            if (metric.Value < floor)
            {
                findings.Add(new WorkloadFinding(
                    "Baseline",
                    metric.Name,
                    $"{metric.Value:F2}{metric.Unit} regressed below baseline {baseline.Baseline.Value:F2}{metric.Unit} by more than {baseline.MaxRegressionPercent.Value:F1}%."));
            }
        }
        else
        {
            var ceiling = baseline.Baseline.Value * (1d + allowed);
            if (metric.Value > ceiling)
            {
                findings.Add(new WorkloadFinding(
                    "Baseline",
                    metric.Name,
                    $"{metric.Value:F2}{metric.Unit} regressed above baseline {baseline.Baseline.Value:F2}{metric.Unit} by more than {baseline.MaxRegressionPercent.Value:F1}%."));
            }
        }
    }

    public static string BuildReproCommand(string scenario, WorkloadExecutionContext context) =>
        $"dotnet run --project /home/runner/work/Clockworks/Clockworks/demo/Clockworks.Demo -- {scenario} --mode {context.RuntimeMode} --seed {context.Seed}";

    private static void PrintHelp()
    {
        Console.WriteLine("Clockworks workload runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project demo/Clockworks.Demo -- workloads [scenario|all] [--mode simulated|system] [--seed N] [--baseline path] [--output path]");
        Console.WriteLine();
        Console.WriteLine("Scenarios:");
        foreach (var scenario in Scenarios.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {scenario.Name,-24} {scenario.Description}");
        }
    }
}
