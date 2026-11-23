using System.Globalization;
using Runesgalder.Sandbox.Worker;

var options = WorkerArguments.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(
        "usage: Runesgalder.Sandbox.Worker --script <assembly> --data <csv> --out <result.json> " +
        "[--timeout-ms <ms>] [--max-output-bytes <bytes>] [--max-rows <rows>]");
    return 2;
}

try
{
    return await WorkerHost.RunAsync(options);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

internal static class WorkerArguments
{
    public static WorkerOptions? Parse(string[] args)
    {
        string? script = null;
        string? data = null;
        string? output = null;
        var timeout = TimeSpan.FromSeconds(5);
        var maxOutputBytes = 64L * 1024;
        var maxRows = 100_000;

        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length)
            {
                return null;
            }

            var value = args[++i];
            switch (args[i - 1])
            {
                case "--script":
                    script = value;
                    break;
                case "--data":
                    data = value;
                    break;
                case "--out":
                    output = value;
                    break;
                case "--timeout-ms" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds):
                    timeout = TimeSpan.FromMilliseconds(milliseconds);
                    break;
                case "--max-output-bytes" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes):
                    maxOutputBytes = bytes;
                    break;
                case "--max-rows" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows):
                    maxRows = rows;
                    break;
                default:
                    return null;
            }
        }

        if (script is null || data is null || output is null)
        {
            return null;
        }

        return new WorkerOptions(script, data, output)
        {
            Timeout = timeout,
            MaxOutputBytes = maxOutputBytes,
            MaxRows = maxRows
        };
    }
}
