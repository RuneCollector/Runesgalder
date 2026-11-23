using System.Diagnostics;

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

    /// <summary>Runs the flow and emits journal events.</summary>
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
        var runId = RunId.New();
        var context = new FlowContext(runId, actualJournal);
        var started = Stopwatch.GetTimestamp();

        await actualJournal.AppendAsync(
            new RunStarted(runId, Name, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var output = await _pipeline(input, context, cancellationToken).ConfigureAwait(false);
            await actualJournal.AppendAsync(
                new RunCompleted(
                    runId,
                    Stopwatch.GetElapsedTime(started),
                    DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await actualJournal.AppendAsync(
                new RunFailed(
                    runId,
                    ErrorCodes.FlowCancelled,
                    "The run was cancelled.",
                    DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await actualJournal.AppendAsync(
                new RunFailed(
                    runId,
                    ErrorCodes.FlowStepFailed,
                    exception.Message,
                    DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
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
