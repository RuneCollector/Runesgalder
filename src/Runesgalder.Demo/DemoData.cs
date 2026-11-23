namespace Runesgalder.Demo;

/// <summary>The synthetic dataset displayed and analysed by the demo.</summary>
internal static class DemoData
{
    /// <summary>The table name.</summary>
    public const string TableName = "sales.csv";

    /// <summary>The column names, in file order.</summary>
    public const string Columns = "month, region, revenue";

    /// <summary>The synthetic CSV content; no customer data is used.</summary>
    public const string Csv =
        """
        month,region,revenue
        2025-01,North,18450
        2025-01,South,12980
        2025-02,North,17210
        2025-02,South,13440
        2025-03,North,20180
        2025-03,South,14860
        2025-04,North,19620
        2025-04,South,15230
        2025-05,North,21340
        2025-05,South,16110
        2025-06,North,22910
        2025-06,South,17480
        2025-07,North,24170
        2025-07,South,18050
        2025-08,North,23640
        2025-08,South,18270
        2025-09,North,25190
        2025-09,South,19410
        2025-10,North,26830
        2025-10,South,20350
        2025-11,North,28460
        2025-11,South,21720
        2025-12,North,31250
        2025-12,South,23890
        """;
}
