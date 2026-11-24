namespace Runesgalder;

/// <summary>Base type for observable run events.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public abstract record RunEvent(RunId RunId, DateTimeOffset Timestamp);

/// <summary>A run has started.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="FlowName">The flow name.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record RunStarted(RunId RunId, string FlowName, DateTimeOffset Timestamp)
    : RunEvent(RunId, Timestamp);

/// <summary>A step has started.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="StepId">The step identifier.</param>
/// <param name="StepName">The step name.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record StepStarted(RunId RunId, StepId StepId, string StepName, DateTimeOffset Timestamp)
    : RunEvent(RunId, Timestamp);

/// <summary>A step completed successfully.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="StepId">The step identifier.</param>
/// <param name="StepName">The step name.</param>
/// <param name="Duration">The step duration.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record StepCompleted(
    RunId RunId,
    StepId StepId,
    string StepName,
    TimeSpan Duration,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A step failed.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="StepId">The step identifier.</param>
/// <param name="StepName">The step name.</param>
/// <param name="ErrorCode">A stable error code.</param>
/// <param name="Message">A human-readable error message.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record StepFailed(
    RunId RunId,
    StepId StepId,
    string StepName,
    string ErrorCode,
    string Message,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A retry has been scheduled after a failed attempt.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="StepId">The step identifier.</param>
/// <param name="Attempt">The next attempt number, starting at one.</param>
/// <param name="ErrorCode">A stable error code.</param>
/// <param name="Message">A human-readable error message.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record StepRetryScheduled(
    RunId RunId,
    StepId StepId,
    int Attempt,
    string ErrorCode,
    string Message,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A model call has started.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="StepId">The step identifier.</param>
/// <param name="StepName">The step name.</param>
/// <param name="ModelId">The provider-reported model identifier, when known.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record ModelCallStarted(
    RunId RunId,
    StepId StepId,
    string StepName,
    string? ModelId,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A model call completed.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="StepId">The step identifier.</param>
/// <param name="StepName">The step name.</param>
/// <param name="ModelId">The provider-reported model identifier, when known.</param>
/// <param name="InputTokens">The reported input token count, when known.</param>
/// <param name="OutputTokens">The reported output token count, when known.</param>
/// <param name="Duration">The model call duration.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record ModelCallCompleted(
    RunId RunId,
    StepId StepId,
    string StepName,
    string? ModelId,
    long? InputTokens,
    long? OutputTokens,
    TimeSpan Duration,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A checkpoint was stored after the flow prefix completed.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Key">The idempotency key.</param>
/// <param name="FlowName">The flow name.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record CheckpointStored(
    RunId RunId,
    string Key,
    string FlowName,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A stored checkpoint replaced flow execution for this run.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Key">The idempotency key.</param>
/// <param name="FlowName">The flow name.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record CheckpointRestored(
    RunId RunId,
    string Key,
    string FlowName,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>Usage crossed a budget warn threshold.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Dimension">The dimension that crossed its threshold.</param>
/// <param name="Limit">The warn threshold; wall values are in seconds.</param>
/// <param name="Used">The usage at the crossing; wall values are in seconds.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record BudgetWarning(
    RunId RunId,
    BudgetDimension Dimension,
    decimal Limit,
    decimal Used,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>Usage crossed a hard budget limit.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Dimension">The dimension that was exceeded.</param>
/// <param name="Limit">The hard limit; wall values are in seconds.</param>
/// <param name="Used">The usage at the crossing; wall values are in seconds.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record BudgetExceeded(
    RunId RunId,
    BudgetDimension Dimension,
    decimal Limit,
    decimal Used,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A governed segment is waiting for a human decision.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="ApprovalId">The approval identifier.</param>
/// <param name="Requirement">The short stable requirement label.</param>
/// <param name="Reason">Human-readable detail about why approval is needed.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record ApprovalRequested(
    RunId RunId,
    ApprovalId ApprovalId,
    string Requirement,
    string Reason,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>An approval request received its final decision.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="ApprovalId">The approval identifier.</param>
/// <param name="Decision">The decision; timeouts are recorded as <see cref="ApprovalDecision.TimedOut"/>.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record ApprovalResolved(
    RunId RunId,
    ApprovalId ApprovalId,
    ApprovalDecision Decision,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

/// <summary>A run completed successfully.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Duration">The total run duration.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record RunCompleted(RunId RunId, TimeSpan Duration, DateTimeOffset Timestamp)
    : RunEvent(RunId, Timestamp);

/// <summary>A run failed.</summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="ErrorCode">A stable error code.</param>
/// <param name="Message">A human-readable error message.</param>
/// <param name="Timestamp">The UTC timestamp.</param>
public sealed record RunFailed(RunId RunId, string ErrorCode, string Message, DateTimeOffset Timestamp)
    : RunEvent(RunId, Timestamp);
