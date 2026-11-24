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

    internal string FlowName { get; init; } = string.Empty;

    /// <summary>Charges token usage to every active budget scope, innermost first.</summary>
    /// <param name="tokens">The token count to charge; not negative.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task; with no active budget scope the call is a no-op.</returns>
    /// <exception cref="BudgetExceededException">Thrown when a scope crosses a hard limit.</exception>
    public ValueTask ChargeTokensAsync(long tokens, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        FlowMetrics.RecordTokens(FlowName, tokens);
        return BudgetRegistry.ChargeAsync(BudgetDimension.Tokens, tokens, cancellationToken);
    }

    /// <summary>Charges cost in US dollars to every active budget scope, innermost first.</summary>
    /// <param name="usd">The cost to charge; not negative.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task; with no active budget scope the call is a no-op.</returns>
    /// <exception cref="BudgetExceededException">Thrown when a scope crosses a hard limit.</exception>
    public ValueTask ChargeUsdAsync(decimal usd, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usd);
        FlowMetrics.RecordUsd(FlowName, usd);
        return BudgetRegistry.ChargeAsync(BudgetDimension.Usd, usd, cancellationToken);
    }
}
