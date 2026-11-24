namespace Runesgalder;

/// <summary>Tracks the active budget scopes of the current asynchronous execution context.</summary>
internal static class BudgetRegistry
{
    private static readonly AsyncLocal<BudgetScope[]?> Current = new();

    public static IDisposable Push(BudgetScope scope)
    {
        var previous = Current.Value;
        Current.Value = previous is null ? [scope] : [.. previous, scope];
        return new Pop(previous);
    }

    public static async ValueTask ChargeAsync(
        BudgetDimension dimension,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var scopes = Current.Value;
        if (scopes is null)
        {
            return;
        }

        for (var index = scopes.Length - 1; index >= 0; index--)
        {
            await scopes[index].ChargeAsync(dimension, amount, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Pop(BudgetScope[]? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>Accumulates usage for one attached budget.</summary>
internal sealed class BudgetScope(Budget budget, FlowContext context)
{
    private readonly object _gate = new();
    private decimal _tokens;
    private decimal _usd;
    private bool _tokensWarned;
    private bool _usdWarned;

    public async ValueTask ChargeAsync(
        BudgetDimension dimension,
        decimal amount,
        CancellationToken cancellationToken)
    {
        decimal used;
        decimal? warn;
        decimal? max;
        var warnNow = false;

        lock (_gate)
        {
            switch (dimension)
            {
                case BudgetDimension.Tokens:
                    _tokens += amount;
                    used = _tokens;
                    warn = budget.WarnTokens;
                    max = budget.MaxTokens;
                    warnNow = warn is { } warnTokens && used > warnTokens && !_tokensWarned;
                    _tokensWarned |= warnNow;
                    break;
                case BudgetDimension.Usd:
                    _usd += amount;
                    used = _usd;
                    warn = budget.WarnUsd;
                    max = budget.MaxUsd;
                    warnNow = warn is { } warnUsd && used > warnUsd && !_usdWarned;
                    _usdWarned |= warnNow;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(dimension),
                        dimension,
                        "Only token and USD usage can be charged.");
            }
        }

        if (warnNow)
        {
            await context.Journal.AppendAsync(
                new BudgetWarning(context.RunId, dimension, warn!.Value, used, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        if (max is { } limit && used > limit)
        {
            await context.Journal.AppendAsync(
                new BudgetExceeded(context.RunId, dimension, limit, used, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw new BudgetExceededException(dimension, limit, used);
        }
    }
}
