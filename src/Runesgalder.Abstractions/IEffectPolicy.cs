namespace Runesgalder;

/// <summary>Evaluates whether an effect may proceed under a policy.</summary>
public interface IEffectPolicy
{
    /// <summary>Evaluates an effect.</summary>
    /// <param name="effect">The effect to evaluate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The policy decision.</returns>
    ValueTask<PolicyDecision> EvaluateAsync(
        EffectDescriptor effect,
        CancellationToken cancellationToken = default);
}
