using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineRunOutcome
{
    Success,
    Failure,
    Cancelled
}

/// <summary>
/// Machine-readable outcome of a single pipeline run (M-08-1.3). Emitted by the
/// pipeline as ci-results/result.json and retrievable by branch. SchemaVersion
/// guards DEVELOPER ingestion: it bumps when this contract changes shape, so old
/// artifacts stay parseable and pipelines do not all have to change on the same day.
/// </summary>
public sealed record PipelineRunResult(
    int SchemaVersion,
    string Repository,
    string Branch,
    string CommitSha,
    string WorkflowRunId,
    PipelineRunOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int TestsTotal,
    int TestsPassed,
    int TestsFailed,
    int TestsSkipped);
