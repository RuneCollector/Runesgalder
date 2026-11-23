namespace Runesgalder;

/// <summary>Identifies a single effect within a run.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct EffectId(Guid Value)
{
    /// <summary>Creates a new unique effect identifier.</summary>
    /// <returns>A new <see cref="EffectId"/>.</returns>
    public static EffectId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
