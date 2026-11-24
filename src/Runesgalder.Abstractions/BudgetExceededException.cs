namespace Runesgalder;

/// <summary>A governed segment crossed a hard budget limit.</summary>
public sealed class BudgetExceededException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="dimension">The dimension that was exceeded.</param>
    /// <param name="limit">The hard limit.</param>
    /// <param name="used">The usage that crossed the limit.</param>
    public BudgetExceededException(BudgetDimension dimension, decimal limit, decimal used)
        : base($"The {dimension} budget was exceeded: used {used} of {limit}.")
    {
        Dimension = dimension;
        Limit = limit;
        Used = used;
    }

    /// <summary>The dimension that was exceeded.</summary>
    public BudgetDimension Dimension { get; }

    /// <summary>The hard limit.</summary>
    public decimal Limit { get; }

    /// <summary>The usage that crossed the limit.</summary>
    public decimal Used { get; }
}
