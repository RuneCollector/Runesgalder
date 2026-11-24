namespace Runesgalder;

/// <summary>Identifies a single approval request.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct ApprovalId(Guid Value)
{
    /// <summary>Creates a new unique approval identifier.</summary>
    /// <returns>A new <see cref="ApprovalId"/>.</returns>
    public static ApprovalId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
