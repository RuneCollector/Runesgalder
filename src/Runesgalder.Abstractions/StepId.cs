namespace Runesgalder;

/// <summary>Identifies a step within a run.</summary>
/// <param name="Value">A stable step name.</param>
public readonly record struct StepId(string Value)
{
    /// <summary>Creates a step identifier from a step name.</summary>
    /// <param name="name">The non-empty step name.</param>
    /// <returns>A step identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty, or whitespace.</exception>
    public static StepId FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new StepId(name);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
