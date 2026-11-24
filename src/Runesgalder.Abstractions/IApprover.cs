namespace Runesgalder;

/// <summary>Obtains a human decision for an approval request.</summary>
public interface IApprover
{
    /// <summary>Requests a decision.</summary>
    /// <param name="request">The approval request.</param>
    /// <param name="cancellationToken">A cancellation token; its expiry signals a timeout.</param>
    /// <returns>The decision; approvers return <see cref="ApprovalDecision.Approved"/> or <see cref="ApprovalDecision.Denied"/>.</returns>
    ValueTask<ApprovalDecision> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);
}
