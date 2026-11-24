namespace Runesgalder;

/// <summary>A request for a human decision before a governed segment runs.</summary>
/// <param name="Id">The approval identifier.</param>
/// <param name="Requirement">A short stable label such as <c>sandbox.execute</c>.</param>
/// <param name="Reason">Human-readable detail about why approval is needed.</param>
public sealed record ApprovalRequest(ApprovalId Id, string Requirement, string Reason);
