using Clockworks.Demo.Workloads;
using Xunit;

namespace Clockworks.Tests;

public sealed class WorkloadRunnerTests
{
    [Fact]
    public void FinalizeResult_marks_baseline_regressions()
    {
        var baselines = new WorkloadBaselineSet(new Dictionary<string, WorkloadScenarioBaseline>(StringComparer.OrdinalIgnoreCase)
        {
            ["sample"] = new(new Dictionary<string, WorkloadMetricBaseline>(StringComparer.OrdinalIgnoreCase)
            {
                ["runtime"] = new(Baseline: 100, MaxRegressionPercent: 25)
            })
        });

        var result = WorkloadCommand.FinalizeResult(
            "sample",
            WorkloadRuntimeMode.Simulated,
            123,
            "summary",
            TimeSpan.FromMilliseconds(140),
            [new WorkloadMetric("runtime", 140, "ms")],
            [new WorkloadInvariant("ok", true, "all good")],
            baselines,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "dotnet run");

        Assert.Equal(WorkloadStatus.Regressed, result.Status);
        Assert.Contains(result.Findings, finding => finding.Category == "Baseline");
    }

    [Fact]
    public async Task Order_pipeline_workload_small_simulated_run_passes()
    {
        var scenario = new OrderPipelineWorkloadScenario(new OrderPipelineSimulationOptions(
            Orders: 20,
            MaxSteps: 5_000,
            TickMs: 5,
            RetryAfterMs: 25,
            DropRate: 0.02,
            DuplicateRate: 0.04,
            ReorderRate: 0.04,
            MaxAdditionalDelayMs: 15,
            DedupeRetentionLimit: 128,
            PruneEvery: 16));

        var result = await scenario.ExecuteAsync(
            new WorkloadExecutionContext(
                WorkloadRuntimeMode.Simulated,
                12345,
                "/tmp/clockworks-tests",
                WorkloadBaselineSet.Empty,
                WriteArtifacts: false),
            CancellationToken.None);

        Assert.Equal(WorkloadStatus.Passed, result.Status);
        Assert.Contains(result.Invariants, invariant => invariant.Name == "all-orders-confirmed" && invariant.Passed);
    }

    [Fact]
    public async Task Causal_fanout_workload_small_simulated_run_passes()
    {
        var scenario = new CausalFanoutFanInWorkloadScenario(nodes: 6, rounds: 10);

        var result = await scenario.ExecuteAsync(
            new WorkloadExecutionContext(
                WorkloadRuntimeMode.Simulated,
                7,
                "/tmp/clockworks-tests",
                WorkloadBaselineSet.Empty,
                WriteArtifacts: false),
            CancellationToken.None);

        Assert.Equal(WorkloadStatus.Passed, result.Status);
        Assert.Contains(result.Invariants, invariant => invariant.Name == "concurrency-detected" && invariant.Passed);
    }
}
