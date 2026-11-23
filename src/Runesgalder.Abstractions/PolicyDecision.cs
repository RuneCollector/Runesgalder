namespace Runesgalder;

/// <summary>The outcome of a policy evaluation.</summary>
/// <param name="IsAllowed">Whether the effect may proceed.</param>
/// <param name="PolicyName">The policy that produced the decision.</param>
/// <param name="Reason">A human-readable explanation.</param>
public sealed record PolicyDecision(bool IsAllowed, string PolicyName, string Reason)
{
    /// <summary>Creates an allow decision.</summary>
    /// <param name="policyName">The policy name.</param>
    /// <param name="reason">The reason for allowing the effect.</param>
    /// <returns>An allow decision.</returns>
    public static PolicyDecision Allow(string policyName, string reason = "Allowed by policy.") =>
        new(true, policyName, reason);

    /// <summary>Creates a deny decision.</summary>
    /// <param name="policyName">The policy name.</param>
    /// <param name="reason">The reason for denying the effect.</param>
    /// <returns>A deny decision.</returns>
    public static PolicyDecision Deny(string policyName, string reason) =>
        new(false, policyName, reason);
}
