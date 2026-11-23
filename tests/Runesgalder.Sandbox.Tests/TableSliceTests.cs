using Runesgalder.Sandbox;

namespace Runesgalder.SandboxTests;

public sealed class TableSliceTests
{
    private static readonly string[] SalesColumns = ["month", "region", "revenue"];

    [Fact]
    public void ToSliceKeepsTheHostTable()
    {
        var table = new FakeTable(
            "sales.csv",
            SalesColumns,
            [
                ["2025-01", "North", "100"],
                ["2025-01", "South", "250"]
            ]);

        var slice = table.ToSlice();

        Assert.Equal("sales.csv", slice.Name);
        Assert.Equal(SalesColumns, slice.Columns);
        Assert.Equal(2, slice.Count);
        Assert.Equal("South", slice.Rows[1][1]);
    }

    [Fact]
    public void FilterKeepsMatchingRowsInOrder()
    {
        var slice = Sales().Filter(row => row[0] == "2025-01");

        Assert.Equal(2, slice.Count);
        Assert.Equal(["100", "300"], slice.Rows.Select(row => row[2]));
    }

    [Fact]
    public void SortByNumberOrdersNumericallyAndKeepsTiesStable()
    {
        var table = new FakeTable(
            "sales.csv",
            ["id", "revenue"],
            [
                ["a", "10"],
                ["b", "2"],
                ["c", "10"],
                ["d", "5"]
            ]);

        var ascending = table.ToSlice().SortByNumber("revenue");
        var descending = table.ToSlice().SortByNumber("revenue", descending: true);

        Assert.Equal(["b", "d", "a", "c"], ascending.Rows.Select(row => row[0]));
        Assert.Equal(["a", "c", "d", "b"], descending.Rows.Select(row => row[0]));
    }

    [Fact]
    public void SortByTextUsesOrdinalOrdering()
    {
        var table = new FakeTable(
            "sales.csv",
            ["region"],
            [
                ["b"],
                ["A"],
                ["a"]
            ]);

        var sorted = table.ToSlice().SortByText("region");

        Assert.Equal(["A", "a", "b"], sorted.Rows.Select(row => row[0]));
    }

    [Fact]
    public void TakeLimitsRowsAndRejectsANegativeCount()
    {
        var slice = Sales();

        Assert.Equal(["100", "300"], slice.Take(2).Rows.Select(row => row[2]));
        Assert.Empty(slice.Take(0).Rows);
        Assert.Equal(3, slice.Take(99).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = slice.Take(-1); });
    }

    [Fact]
    public void GroupByKeepsFirstSeenKeyOrder()
    {
        var groups = Sales().GroupBy("month");

        Assert.Equal(["2025-01", "2025-02"], groups.Select(group => group.Key));
        Assert.Equal(2, groups[0].Table.Count);
        Assert.Equal(1, groups[1].Table.Count);
        Assert.Equal("John", groups[1].Table.Rows[0][1]);
    }

    [Fact]
    public void GroupByTreatsNullAndEmptyKeysAsOneGroup()
    {
        var table = new FakeTable(
            "sales.csv",
            ["region", "revenue"],
            [
                [null, "1"],
                [string.Empty, "2"],
                ["North", "3"]
            ]);

        var groups = table.ToSlice().GroupBy("region");

        Assert.Equal(2, groups.Count);
        Assert.Equal(string.Empty, groups[0].Key);
        Assert.Equal(2, groups[0].Table.Count);
    }

    [Fact]
    public void AggregatesParseInvariantNumbers()
    {
        var slice = Sales();

        Assert.Equal(600m, slice.Sum("revenue"));
        Assert.Equal(200m, slice.Average("revenue"));
        Assert.Equal(100m, slice.Minimum("revenue"));
        Assert.Equal(300m, slice.Maximum("revenue"));
    }

    [Fact]
    public void AggregatesAcceptGroupedRows()
    {
        var monthly = Sales().GroupBy("month");

        Assert.Equal([400m, 200m], monthly.Select(group => group.Table.Sum("revenue")));
    }

    [Fact]
    public void AggregatesRejectNonNumericValuesWithTheirPosition()
    {
        var table = new FakeTable(
            "sales.csv",
            ["revenue"],
            [
                ["100"],
                ["not a number"]
            ]);

        var error = Assert.Throws<FormatException>(() => { _ = table.ToSlice().Sum("revenue"); });

        Assert.Contains("Row 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("'revenue'", error.Message, StringComparison.Ordinal);
        Assert.Contains("not a number", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySlicesSumToZeroAndRejectTheOtherAggregates()
    {
        var empty = Sales().Take(0);

        Assert.Equal(0m, empty.Sum("revenue"));
        Assert.Throws<InvalidOperationException>(() => { _ = empty.Average("revenue"); });
        Assert.Throws<InvalidOperationException>(() => { _ = empty.Minimum("revenue"); });
        Assert.Throws<InvalidOperationException>(() => { _ = empty.Maximum("revenue"); });
    }

    [Fact]
    public void UnknownColumnsAreRejected()
    {
        var slice = Sales();

        Assert.Throws<ArgumentException>(() => { _ = slice.GroupBy("missing"); });
        Assert.Throws<ArgumentException>(() => { _ = slice.SortByText("missing"); });
        Assert.Throws<ArgumentException>(() => { _ = slice.Sum("missing"); });
    }

    [Fact]
    public void RowsMustMatchTheColumnCount()
    {
        Assert.Throws<ArgumentException>(() =>
            _ = new TableSlice("sales.csv", SalesColumns, [["2025-01", "North"]]));
    }

    private static TableSlice Sales()
    {
        var table = new FakeTable(
            "sales.csv",
            SalesColumns,
            [
                ["2025-01", "North", "100"],
                ["2025-01", "South", "300"],
                ["2025-02", "John", "200"]
            ]);

        return table.ToSlice();
    }

    private sealed class FakeTable(
        string name,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows) : ITable
    {
        public string Name { get; } = name;

        public IReadOnlyList<string> Columns { get; } = columns;

        public IReadOnlyList<IReadOnlyList<string?>> Rows { get; } = rows;
    }
}
