namespace Runesgalder;

/// <summary>The outcome of an approval request.</summary>
public enum ApprovalDecision
{
    /// <summary>The segment may run.</summary>
    Approved,

    /// <summary>The segment must not run.</summary>
    Denied,

    /// <summary>The approver did not answer in time; the runtime alone produces this value.</summary>
    TimedOut,
}
