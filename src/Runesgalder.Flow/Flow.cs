using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Runesgalder;

/// <summary>A typed agent flow with LINQ-style composition.</summary>
/// <typeparam name="TIn">The flow input type.</typeparam>
/// <typeparam name="TOut">The flow output type.</typeparam>
public sealed class Flow<TIn, TOut>
{
    private readonly Func<TIn, FlowContext, CancellationToken, ValueTask<TOut>> _pipeline;

    internal Flow(
        string name,
        Func<TIn, FlowContext, CancellationToken, ValueTask<TOut>> pipeline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(pipeline);
        Name = name;
        _pipeline = pipeline;
    }

    /// <summary>The flow name.</summary>
    public string Name { get; }

    /// <summary>Runs the flow to completion and emits journal events.</summary>
    /// <param name="input">The flow input.</param>
    /// <param name="journal">The journal to write to; a fresh in-memory journal is used when omitted.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The flow output.</returns>
    public async Task<TOut> RunAsync(
        TIn input,
        IJournal? journal = null,
        CancellationToken cancellationToken = default)
    {
        var actualJournal = journal ?? new InMemoryJournal();
        return await RunCoreAsync(input, RunId.New(), actualJournal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the flow and returns a live event stream alongside the result.</summary>
    /// <param name="input">The flow input.</param>
    /// <param name="journal">An additional journal to receive every event; a fresh in-memory journal is used when omitted.</param>
    /// <param name="cancellationToken">A cancellation token that cancels the run itself.</param>
    /// <returns>A handle with the run identifier, event stream, and result.</returns>
    public FlowRun<TOut> BeginRun(
        TIn input,
        IJournal? journal = null,
        CancellationToken cancellationToken = default)
    {
        var actualJournal = journal ?? new InMemoryJournal();
        var runId = RunId.New();
        var tee = new TeeJournal(actualJournal);
        var result = RunCoreAsync(input, runId, tee, cancellationToken);
        return new FlowRun<TOut>(runId, tee.Events, CompleteWhenFinishedAsync(result, tee));
    }

    /// <summary>Composes this flow with a following step.</summary>
    /// <typeparam name="TNext">The next step output type.</typeparam>
    /// <param name="next">The next step.</param>
    /// <returns>A composed flow.</returns>
    public Flow<TIn, TNext> Then<TNext>(Step<TOut, TNext> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var previous = _pipeline;
        return new Flow<TIn, TNext>(
            $"{Name} -> {next.Name}",
            async (input, context, cancellationToken) =>
            {
                var intermediate = await previous(input, context, cancellationToken).ConfigureAwait(false);
                return await next.RunAsync(intermediate, context, cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>Retries the whole flow when it throws.</summary>
    /// <param name="maxAttempts">The maximum number of attempts, at least one.</param>
    /// <param name="delay">The delay between attempts.</param>
    /// <returns>A flow with retry semantics.</returns>
    public Flow<TIn, TOut> Retry(int maxAttempts, TimeSpan delay = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        var pipeline = _pipeline;
        var stepId = StepId.FromName(Name);
        var flowName = Name;

        return new Flow<TIn, TOut>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await pipeline(input, context, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (attempt < maxAttempts)
                    {
                        await context.Journal.AppendAsync(
                            new StepRetryScheduled(
                                context.RunId,
                                stepId,
                                attempt + 1,
                                ErrorCodes.FlowStepFailed,
                                exception.Message,
                                DateTimeOffset.UtcNow),
                            cancellationToken).ConfigureAwait(false);

                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            });
    }

    /// <summary>
    /// Persists the output of this flow prefix so a later run with the same idempotency key
    /// returns it instead of repeating model calls or other effects.
    /// </summary>
    /// <param name="store">The checkpoint store.</param>
    /// <param name="idempotencyKey">
    /// The key that scopes the checkpoint; when omitted, a key is derived from the flow name and a hash of the input.
    /// </param>
    /// <returns>A flow with checkpoint semantics.</returns>
    public Flow<TIn, TOut> Checkpoint(ICheckpointStore store, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (idempotencyKey is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        }

        var pipeline = _pipeline;
        var flowName = Name;
        var outputType = typeof(TOut).FullName
            ?? throw new InvalidOperationException($"The output type {typeof(TOut)} has no full name.");

        return new Flow<TIn, TOut>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                var key = idempotencyKey ?? $"{flowName}:{Fingerprint(input)}";
                var existing = await store.FindAsync(key, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    await context.Journal.AppendAsync(
                        new CheckpointRestored(context.RunId, key, flowName, DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                    return Restore(existing, outputType);
                }

                var output = await pipeline(input, context, cancellationToken).ConfigureAwait(false);
                var json = JsonSerializer.Serialize(output, CheckpointJson.Options);
                await store.SaveAsync(
                    new Checkpoint(key, flowName, outputType, json, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                await context.Journal.AppendAsync(
                    new CheckpointStored(context.RunId, key, flowName, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                return output;
            });
    }

    private async Task<TOut> RunCoreAsync(
        TIn input,
        RunId runId,
        IJournal journal,
        CancellationToken cancellationToken)
    {
        var context = new FlowContext(runId, journal);
        var started = Stopwatch.GetTimestamp();

        using var activity = FlowDiagnostics.ActivitySource.StartActivity(
            $"flow {Name}",
            ActivityKind.Internal);
        activity?.SetTag(FlowDiagnostics.RunIdTag, runId.ToString());
        activity?.SetTag(FlowDiagnostics.FlowNameTag, Name);

        await journal.AppendAsync(
            new RunStarted(runId, Name, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var output = await _pipeline(input, context, cancellationToken).ConfigureAwait(false);
            await journal.AppendAsync(
                new RunCompleted(runId, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowCancelled, "The run was cancelled.", DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowStepFailed, exception.Message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<TOut> CompleteWhenFinishedAsync(Task<TOut> result, TeeJournal tee)
    {
        try
        {
            return await result.ConfigureAwait(false);
        }
        finally
        {
            tee.Complete();
        }
    }

    private static string Fingerprint(TIn input)
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(input, CheckpointJson.Options);
            return Convert.ToHexStringLower(SHA256.HashData(json));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                "The flow input cannot be hashed for a default checkpoint key; pass an explicit idempotency key.",
                exception);
        }
    }

    private static TOut Restore(Checkpoint checkpoint, string outputType)
    {
        if (!string.Equals(checkpoint.OutputType, outputType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checkpoint '{checkpoint.Key}' holds a '{checkpoint.OutputType}' but this flow produces '{outputType}'.");
        }

        var value = JsonSerializer.Deserialize<TOut>(checkpoint.OutputJson, CheckpointJson.Options);
        return value!;
    }
}

/// <summary>Starts flows.</summary>
public static class Flow
{
    /// <summary>Starts a flow with a first step.</summary>
    /// <typeparam name="TIn">The flow input type.</typeparam>
    /// <typeparam name="TOut">The flow output type.</typeparam>
    /// <param name="first">The first step.</param>
    /// <returns>A flow.</returns>
    public static Flow<TIn, TOut> Start<TIn, TOut>(Step<TIn, TOut> first)
    {
        ArgumentNullException.ThrowIfNull(first);
        return new Flow<TIn, TOut>(first.Name, first.RunAsync);
    }

    /// <summary>Starts a named flow from a raw pipeline.</summary>
    /// <typeparam name="TIn">The flow input type.</typeparam>
    /// <typeparam name="TOut">The flow output type.</typeparam>
    /// <param name="name">The flow name.</param>
    /// <param name="pipeline">The pipeline body.</param>
    /// <returns>A flow.</returns>
    public static Flow<TIn, TOut> Start<TIn, TOut>(
        string name,
        Func<TIn, FlowContext, CancellationToken, ValueTask<TOut>> pipeline) =>
        new(name, pipeline);
}

internal static class CheckpointJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
