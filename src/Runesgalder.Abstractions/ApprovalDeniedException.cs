namespace Runesgalder;

/// <summary>An approval request did not allow the governed segment to run.</summary>
public sealed class ApprovalDeniedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="approvalId">The approval identifier.</param>
    /// <param name="decision">The denial or timeout decision.</param>
    public ApprovalDeniedException(ApprovalId approvalId, ApprovalDecision decision)
        : base(decision == ApprovalDecision.TimedOut
            ? $"The approval request '{approvalId}' timed out and was denied."
            : $"The approval request '{approvalId}' was denied.")
    {
        ApprovalId = approvalId;
        Decision = decision;
    }

    /// <summary>The approval identifier.</summary>
    public ApprovalId ApprovalId { get; }

    /// <summary>The denial or timeout decision.</summary>
    public ApprovalDecision Decision { get; }
}
