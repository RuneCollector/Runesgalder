using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

namespace Runesgalder.Sandbox.Worker;

/// <summary>Loads a compiled analysis, runs it against a CSV table, and records the result.</summary>
public static class WorkerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs one analysis job.</summary>
    /// <param name="options">The job inputs.</param>
    /// <param name="cancellationToken">A token that aborts the worker itself.</param>
    /// <returns>0 on success, or a non-zero exit code that matches the error kind.</returns>
    public static async Task<int> RunAsync(WorkerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        WorkerResult result;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await ExecuteAsync(options, stopwatch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CsvFormatException exception)
        {
            result = Failure(WorkerResult.ErrorKinds.Input, exception.Message, stopwatch);
        }
        catch (OutputQuotaExceededException exception)
        {
            result = Failure(WorkerResult.ErrorKinds.OutputQuota, exception.Message, stopwatch);
        }
        catch (InvalidOperationException exception)
        {
            result = Failure(WorkerResult.ErrorKinds.Contract, exception.Message, stopwatch);
        }

        await WriteResultAsync(options.OutputPath, result, cancellationToken);
        return ExitCode(result);
    }

    private static async Task<WorkerResult> ExecuteAsync(
        WorkerOptions options,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var dataBytes = ReadData(options);
        var table = CsvTable.Parse(Path.GetFileNameWithoutExtension(options.DataPath), dataBytes, options.MaxRows);
        var analysis = CreateAnalysis(options.ScriptPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        var context = new AnalysisScriptContext(options.MaxOutputBytes, options.Deterministic, timeout.Token);

        AnalysisResult? analysisResult;
        try
        {
            analysisResult = await analysis.RunAsync(table, context);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Failure(
                WorkerResult.ErrorKinds.Timeout,
                string.Create(CultureInfo.InvariantCulture, $"The analysis exceeded {options.Timeout.TotalMilliseconds} ms."),
                stopwatch);
        }
        catch (OutputQuotaExceededException exception)
        {
            return Failure(WorkerResult.ErrorKinds.OutputQuota, exception.Message, stopwatch);
        }
        catch (Exception exception)
        {
            return Failure(
                WorkerResult.ErrorKinds.Script,
                Truncate($"{exception.GetType().Name}: {exception.Message}"),
                stopwatch);
        }

        if (analysisResult is null)
        {
            return Failure(WorkerResult.ErrorKinds.Script, "The analysis returned null.", stopwatch);
        }

        var charts = context.Charts
            .Select(chart => new ChartResult(chart.Kind, chart.Title, chart.Labels, chart.Values))
            .ToArray();

        return new WorkerResult(
            true,
            null,
            null,
            analysisResult.Summary,
            charts,
            [.. context.Logs],
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private static string ReadData(WorkerOptions options)
    {
        var info = new FileInfo(options.DataPath);
        if (!info.Exists)
        {
            throw new CsvFormatException($"The data file '{options.DataPath}' does not exist.");
        }

        if (info.Length > options.MaxDataBytes)
        {
            throw new CsvFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The data file is {info.Length} bytes, above the {options.MaxDataBytes} byte limit."));
        }

        try
        {
            return File.ReadAllText(options.DataPath, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CsvFormatException($"The data file '{options.DataPath}' could not be read.", exception);
        }
    }

    private static IAnalysis CreateAnalysis(string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException($"The script assembly '{scriptPath}' does not exist.");
        }

        Type[] types;
        try
        {
            var loadContext = new ScriptLoadContext();
            types = loadContext.LoadFromAssemblyPath(Path.GetFullPath(scriptPath)).GetTypes();
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or ReflectionTypeLoadException)
        {
            throw new InvalidOperationException("The script assembly could not be loaded.", exception);
        }

        var implementers = types
            .Where(type => typeof(IAnalysis).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .ToArray();

        if (implementers.Length != 1)
        {
            throw new InvalidOperationException(
                $"The script assembly must contain exactly one {typeof(IAnalysis).FullName} implementation; found {implementers.Length}.");
        }

        object? instance;
        try
        {
            instance = Activator.CreateInstance(implementers[0]);
        }
        catch (Exception exception) when (exception is MissingMethodException or TargetInvocationException or MemberAccessException)
        {
            throw new InvalidOperationException(
                $"The type '{implementers[0].FullName}' needs a public parameterless constructor.",
                exception);
        }

        return instance as IAnalysis
            ?? throw new InvalidOperationException($"The type '{implementers[0].FullName}' is not an {nameof(IAnalysis)}.");
    }

    private static WorkerResult Failure(string kind, string message, Stopwatch stopwatch) =>
        new(false, kind, Truncate(message), null, [], [], stopwatch.Elapsed.TotalMilliseconds);

    private static string Truncate(string message) =>
        message.Length <= 4096 ? message : string.Concat(message.AsSpan(0, 4096), "…");

    private static async Task WriteResultAsync(
        string outputPath,
        WorkerResult result,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    private static int ExitCode(WorkerResult result)
    {
        if (result.Success)
        {
            return 0;
        }

        return result.Error switch
        {
            WorkerResult.ErrorKinds.Input => 2,
            WorkerResult.ErrorKinds.Timeout => 3,
            WorkerResult.ErrorKinds.Contract => 4,
            WorkerResult.ErrorKinds.OutputQuota => 5,
            _ => 1
        };
    }

    private sealed class ScriptLoadContext() : AssemblyLoadContext("Runesgalder.Script", isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var contractAssembly = typeof(IAnalysis).Assembly;
            return string.Equals(assemblyName.Name, contractAssembly.GetName().Name, StringComparison.Ordinal)
                ? contractAssembly
                : null;
        }
    }
}
