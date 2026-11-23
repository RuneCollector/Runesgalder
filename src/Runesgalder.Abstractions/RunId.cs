namespace Runesgalder;

/// <summary>Identifies a single governed run.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct RunId(Guid Value)
{
    /// <summary>Creates a new unique run identifier.</summary>
    /// <returns>A new <see cref="RunId"/>.</returns>
    public static RunId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
