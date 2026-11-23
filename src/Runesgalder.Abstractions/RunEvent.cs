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
