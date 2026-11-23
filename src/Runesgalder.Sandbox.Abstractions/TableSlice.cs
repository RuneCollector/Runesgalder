using System.Globalization;

namespace Runesgalder.Sandbox;

/// <summary>A stable, read-only slice of a host table.</summary>
public sealed class TableSlice
{
    private readonly IReadOnlyList<IReadOnlyList<string?>> _rows;

    /// <summary>Creates a slice over the supplied rows.</summary>
    /// <param name="name">The table name.</param>
    /// <param name="columns">The column names.</param>
    /// <param name="rows">The rows; every row must have one value per column.</param>
    /// <exception cref="ArgumentException">A row does not match the column count.</exception>
    public TableSlice(string name, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is null || rows[index].Count != columns.Count)
            {
                throw new ArgumentException(
                    $"Row {index} has a different number of values than the {columns.Count} columns.",
                    nameof(rows));
            }
        }

        Name = name;
        Columns = columns;
        _rows = rows;
    }

    /// <summary>The table name.</summary>
    public string Name { get; }

    /// <summary>The column names.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The rows, each with one string value per column.</summary>
    public IReadOnlyList<IReadOnlyList<string?>> Rows => _rows;

    /// <summary>The number of rows.</summary>
    public int Count => _rows.Count;

    /// <summary>Keeps the rows that satisfy the predicate, in their original order.</summary>
    /// <param name="predicate">The predicate applied to each row.</param>
    /// <returns>A new slice with the matching rows.</returns>
    public TableSlice Filter(Func<IReadOnlyList<string?>, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var kept = new List<IReadOnlyList<string?>>();
        foreach (var row in _rows)
        {
            if (predicate(row))
            {
                kept.Add(row);
            }
        }

        return new TableSlice(Name, Columns, kept);
    }

    /// <summary>Sorts the rows by a column, comparing values as text with ordinal ordering.</summary>
    /// <param name="column">The column name.</param>
    /// <param name="descending">Whether to sort from high to low.</param>
    /// <returns>A new, stable sorted slice.</returns>
    public TableSlice SortByText(string column, bool descending = false)
    {
        var columnIndex = IndexOf(column);
        var ordered = descending
            ? _rows.OrderByDescending(row => row[columnIndex], StringComparer.Ordinal)
            : _rows.OrderBy(row => row[columnIndex], StringComparer.Ordinal);

        return new TableSlice(Name, Columns, ordered.ToArray());
    }

    /// <summary>Sorts the rows by a column, parsing values as invariant-culture numbers.</summary>
    /// <param name="column">The column name.</param>
    /// <param name="descending">Whether to sort from high to low.</param>
    /// <returns>A new, stable sorted slice.</returns>
    /// <exception cref="FormatException">A value in the column is not a number.</exception>
    public TableSlice SortByNumber(string column, bool descending = false)
    {
        var columnIndex = IndexOf(column);
        var keyed = new List<(IReadOnlyList<string?> Row, decimal Value)>(_rows.Count);
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            keyed.Add((_rows[rowIndex], NumberAt(_rows[rowIndex], columnIndex, column, rowIndex)));
        }

        var ordered = descending
            ? keyed.OrderByDescending(entry => entry.Value)
            : keyed.OrderBy(entry => entry.Value);

        return new TableSlice(Name, Columns, ordered.Select(entry => entry.Row).ToArray());
    }

    /// <summary>Keeps at most the given number of leading rows.</summary>
    /// <param name="count">The maximum number of rows; zero returns an empty slice.</param>
    /// <returns>A new slice.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
    public TableSlice Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count >= _rows.Count)
        {
            return this;
        }

        var taken = new IReadOnlyList<string?>[count];
        for (var index = 0; index < count; index++)
        {
            taken[index] = _rows[index];
        }

        return new TableSlice(Name, Columns, taken);
    }

    /// <summary>Groups the rows by a column, keeping keys in first-appearance order.</summary>
    /// <param name="column">The column name.</param>
    /// <returns>The groups, each holding its rows in their original order.</returns>
    public IReadOnlyList<TableGroup> GroupBy(string column)
    {
        var columnIndex = IndexOf(column);
        var keys = new List<string>();
        var buckets = new Dictionary<string, List<IReadOnlyList<string?>>>(StringComparer.Ordinal);

        foreach (var row in _rows)
        {
            var key = row[columnIndex] ?? string.Empty;
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets.Add(key, bucket);
                keys.Add(key);
            }

            bucket.Add(row);
        }

        var groups = new TableGroup[keys.Count];
        for (var index = 0; index < keys.Count; index++)
        {
            groups[index] = new TableGroup(keys[index], new TableSlice(Name, Columns, buckets[keys[index]]));
        }

        return groups;
    }

    /// <summary>Sums a numeric column; an empty slice sums to zero.</summary>
    /// <param name="column">The column name.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="FormatException">A value in the column is not a number.</exception>
    public decimal Sum(string column)
    {
        var columnIndex = IndexOf(column);
        var sum = 0m;
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            sum += NumberAt(_rows[rowIndex], columnIndex, column, rowIndex);
        }

        return sum;
    }

    /// <summary>Averages a numeric column.</summary>
    /// <param name="column">The column name.</param>
    /// <returns>The average.</returns>
    /// <exception cref="InvalidOperationException">The slice has no rows.</exception>
    /// <exception cref="FormatException">A value in the column is not a number.</exception>
    public decimal Average(string column)
    {
        if (_rows.Count == 0)
        {
            throw new InvalidOperationException($"Table '{Name}' has no rows to average.");
        }

        var columnIndex = IndexOf(column);
        var sum = 0m;
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            sum += NumberAt(_rows[rowIndex], columnIndex, column, rowIndex);
        }

        return sum / _rows.Count;
    }

    /// <summary>Finds the smallest value in a numeric column.</summary>
    /// <param name="column">The column name.</param>
    /// <returns>The minimum.</returns>
    /// <exception cref="InvalidOperationException">The slice has no rows.</exception>
    /// <exception cref="FormatException">A value in the column is not a number.</exception>
    public decimal Minimum(string column)
    {
        if (_rows.Count == 0)
        {
            throw new InvalidOperationException($"Table '{Name}' has no rows to take a minimum from.");
        }

        var columnIndex = IndexOf(column);
        var minimum = NumberAt(_rows[0], columnIndex, column, 0);
        for (var rowIndex = 1; rowIndex < _rows.Count; rowIndex++)
        {
            var value = NumberAt(_rows[rowIndex], columnIndex, column, rowIndex);
            if (value < minimum)
            {
                minimum = value;
            }
        }

        return minimum;
    }

    /// <summary>Finds the largest value in a numeric column.</summary>
    /// <param name="column">The column name.</param>
    /// <returns>The maximum.</returns>
    /// <exception cref="InvalidOperationException">The slice has no rows.</exception>
    /// <exception cref="FormatException">A value in the column is not a number.</exception>
    public decimal Maximum(string column)
    {
        if (_rows.Count == 0)
        {
            throw new InvalidOperationException($"Table '{Name}' has no rows to take a maximum from.");
        }

        var columnIndex = IndexOf(column);
        var maximum = NumberAt(_rows[0], columnIndex, column, 0);
        for (var rowIndex = 1; rowIndex < _rows.Count; rowIndex++)
        {
            var value = NumberAt(_rows[rowIndex], columnIndex, column, rowIndex);
            if (value > maximum)
            {
                maximum = value;
            }
        }

        return maximum;
    }

    private int IndexOf(string column)
    {
        for (var index = 0; index < Columns.Count; index++)
        {
            if (string.Equals(Columns[index], column, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ArgumentException($"Column '{column}' does not exist in table '{Name}'.", nameof(column));
    }

    private static decimal NumberAt(
        IReadOnlyList<string?> row,
        int columnIndex,
        string column,
        int rowIndex)
    {
        var value = row[columnIndex];
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        throw new FormatException(
            $"Row {rowIndex}, column '{column}' is not a number: \"{value}\".");
    }
}

/// <summary>One group produced by <see cref="TableSlice.GroupBy"/>.</summary>
/// <param name="Key">The shared value; null and empty values are grouped together as empty.</param>
/// <param name="Table">The rows that share the key.</param>
public sealed record TableGroup(string Key, TableSlice Table);

/// <summary>Helpers for reading host tables in generated analysis code.</summary>
public static class TableExtensions
{
    /// <summary>Creates a read-only slice with filtering, grouping, and aggregation helpers.</summary>
    /// <param name="table">The host table.</param>
    /// <returns>A slice over the table.</returns>
    public static TableSlice ToSlice(this ITable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new TableSlice(table.Name, table.Columns, table.Rows);
    }
}
