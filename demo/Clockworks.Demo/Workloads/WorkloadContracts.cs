using System.Text.Json.Serialization;

namespace Clockworks.Demo.Workloads;

public enum WorkloadRuntimeMode
{
    Simulated,
    System,
    Both
}

public enum WorkloadStatus
{
    Passed,
    Failed,
    Regressed,
    Skipped
}

public sealed record WorkloadMetric(
    string Name,
    double Value,
    string Unit,
    bool HigherIsBetter = false,
    string? Description = null);

public sealed record WorkloadInvariant(
    string Name,
    bool Passed,
    string Details);

public sealed record WorkloadFinding(
    string Category,
    string Title,
    string Details);

public sealed record WorkloadMetricBaseline(
    double? Baseline = null,
    double? Min = null,
    double? Max = null,
    double? MaxRegressionPercent = null,
    bool HigherIsBetter = false);

public sealed record WorkloadScenarioBaseline(
    Dictionary<string, WorkloadMetricBaseline> Metrics);

public sealed record WorkloadBaselineSet(
    Dictionary<string, WorkloadScenarioBaseline> Scenarios)
{
    public static readonly WorkloadBaselineSet Empty =
        new(new Dictionary<string, WorkloadScenarioBaseline>(StringComparer.OrdinalIgnoreCase));
}

public sealed record WorkloadExecutionContext(
    WorkloadRuntimeMode RuntimeMode,
    int Seed,
    string OutputDirectory,
    WorkloadBaselineSet Baselines,
    bool WriteArtifacts = true);

public sealed record WorkloadResult(
    string Scenario,
    WorkloadRuntimeMode RuntimeMode,
    int Seed,
    WorkloadStatus Status,
    string Summary,
    double DurationMs,
    IReadOnlyList<WorkloadMetric> Metrics,
    IReadOnlyList<WorkloadInvariant> Invariants,
    IReadOnlyList<WorkloadFinding> Findings,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string ReproductionCommand);

public sealed record WorkloadRunReport(
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string Host,
    IReadOnlyList<WorkloadResult> Results)
{
    [JsonIgnore]
    public bool HasFailures => Results.Any(r => r.Status is WorkloadStatus.Failed or WorkloadStatus.Regressed);
}

public interface IWorkloadScenario
{
    string Name { get; }
    string Description { get; }
    WorkloadRuntimeMode SupportedModes { get; }
    Task<WorkloadResult> ExecuteAsync(WorkloadExecutionContext context, CancellationToken cancellationToken);
}
