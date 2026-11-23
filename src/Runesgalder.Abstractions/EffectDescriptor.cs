namespace Runesgalder;

/// <summary>Describes an effect before it is executed or denied.</summary>
/// <param name="Id">The effect identifier.</param>
/// <param name="Kind">The effect kind.</param>
/// <param name="Name">A stable, human-readable effect name.</param>
public sealed record EffectDescriptor(EffectId Id, EffectKind Kind, string Name);
