using System.Diagnostics;

namespace Runesgalder;

/// <summary>A typed, named unit of work inside a flow.</summary>
/// <typeparam name="TIn">The step input type.</typeparam>
/// <typeparam name="TOut">The step output type.</typeparam>
public abstract class Step<TIn, TOut>
{
    private readonly StepId _id;

    /// <summary>Creates a step.</summary>
    /// <param name="name">The non-empty step name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty, or whitespace.</exception>
    protected Step(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _id = StepId.FromName(name);
    }

    /// <summary>The step name.</summary>
    public string Name { get; }

    /// <summary>The step identifier.</summary>
    public StepId Id => _id;

    /// <summary>Executes the step body.</summary>
    /// <param name="input">The step input.</param>
    /// <param name="context">The run context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The step output.</returns>
    public abstract ValueTask<TOut> ExecuteAsync(
        TIn input,
        FlowContext context,
        CancellationToken cancellationToken);

    /// <summary>Runs the step and emits lifecycle events.</summary>
    /// <param name="input">The step input.</param>
    /// <param name="context">The run context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The step output.</returns>
    public async ValueTask<TOut> RunAsync(
        TIn input,
        FlowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = Stopwatch.GetTimestamp();
        var startedAt = DateTimeOffset.UtcNow;

        using var activity = FlowDiagnostics.ActivitySource.StartActivity(Name, ActivityKind.Internal);
        activity?.SetTag(FlowDiagnostics.RunIdTag, context.RunId.ToString());
        activity?.SetTag(FlowDiagnostics.StepIdTag, _id.ToString());

        await context.Journal.AppendAsync(
            new StepStarted(context.RunId, _id, Name, startedAt),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var output = await ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
            await context.Journal.AppendAsync(
                new StepCompleted(
                    context.RunId,
                    _id,
                    Name,
                    Stopwatch.GetElapsedTime(started),
                    DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            await context.Journal.AppendAsync(
                new StepFailed(
                    context.RunId,
                    _id,
                    Name,
                    ErrorCodes.FlowCancelled,
                    "The step was cancelled.",
                    DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            await context.Journal.AppendAsync(
                new StepFailed(
                    context.RunId,
                    _id,
                    Name,
                    ErrorCodes.FlowStepFailed,
                    exception.Message,
                    DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>Creates delegate-backed steps.</summary>
public static class Step
{
    /// <summary>Defines a step from an asynchronous body.</summary>
    /// <typeparam name="TIn">The step input type.</typeparam>
    /// <typeparam name="TOut">The step output type.</typeparam>
    /// <param name="name">The step name.</param>
    /// <param name="body">The step body.</param>
    /// <returns>A step.</returns>
    public static Step<TIn, TOut> Define<TIn, TOut>(
        string name,
        Func<TIn, FlowContext, CancellationToken, ValueTask<TOut>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new DelegateStep<TIn, TOut>(name, body);
    }

    /// <summary>Defines a synchronous step.</summary>
    /// <typeparam name="TIn">The step input type.</typeparam>
    /// <typeparam name="TOut">The step output type.</typeparam>
    /// <param name="name">The step name.</param>
    /// <param name="body">The step body.</param>
    /// <returns>A step.</returns>
    public static Step<TIn, TOut> Define<TIn, TOut>(string name, Func<TIn, TOut> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new DelegateStep<TIn, TOut>(
            name,
            (input, _, _) => ValueTask.FromResult(body(input)));
    }

    private sealed class DelegateStep<TIn, TOut>(
        string name,
        Func<TIn, FlowContext, CancellationToken, ValueTask<TOut>> body)
        : Step<TIn, TOut>(name)
    {
        public override ValueTask<TOut> ExecuteAsync(
            TIn input,
            FlowContext context,
            CancellationToken cancellationToken) => body(input, context, cancellationToken);
    }
}
