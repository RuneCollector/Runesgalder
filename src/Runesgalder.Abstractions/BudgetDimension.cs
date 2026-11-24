namespace Runesgalder;

/// <summary>The cost dimensions a budget can govern.</summary>
public enum BudgetDimension
{
    /// <summary>Tokens charged explicitly by host adapters.</summary>
    Tokens,

    /// <summary>US dollars charged explicitly by host adapters.</summary>
    Usd,

    /// <summary>Wall-clock time of the governed segment, in seconds.</summary>
    Wall,
}
