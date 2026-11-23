namespace Runesgalder;

/// <summary>Mutable per-run context passed to every step.</summary>
public sealed class FlowContext
{
    /// <summary>Creates a run context.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="journal">The run journal.</param>
    public FlowContext(RunId runId, IJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        RunId = runId;
        Journal = journal;
    }

    /// <summary>The run identifier.</summary>
    public RunId RunId { get; }

    /// <summary>The run journal.</summary>
    public IJournal Journal { get; }

    /// <summary>Arbitrary state shared between steps in the same run.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
