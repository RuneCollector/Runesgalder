using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>Chooses between two steps using this flow's output.</summary>
    /// <typeparam name="TNext">The chosen step's output type.</typeparam>
    /// <param name="predicate">Selects the true branch when it returns <see langword="true"/>.</param>
    /// <param name="whenTrue">The step to run when the predicate returns <see langword="true"/>.</param>
    /// <param name="whenFalse">The step to run when the predicate returns <see langword="false"/>.</param>
    /// <returns>A flow that runs exactly one of the two steps.</returns>
    public Flow<TIn, TNext> Branch<TNext>(
        Func<TOut, bool> predicate,
        Step<TOut, TNext> whenTrue,
        Step<TOut, TNext> whenFalse)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(whenTrue);
        ArgumentNullException.ThrowIfNull(whenFalse);
        var previous = _pipeline;
        return new Flow<TIn, TNext>(
            $"{Name} ? {whenTrue.Name} : {whenFalse.Name}",
            async (input, context, cancellationToken) =>
            {
                var intermediate = await previous(input, context, cancellationToken).ConfigureAwait(false);
                var chosen = predicate(intermediate) ? whenTrue : whenFalse;
                return await chosen.RunAsync(intermediate, context, cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>Runs every branch concurrently with this flow's output and collects the results in declaration order.</summary>
    /// <param name="branches">The branches; at least one, and none may be null.</param>
    /// <returns>A flow that produces one result per branch.</returns>
    /// <remarks>
    /// The first failure stops the run: sibling branches observe a cancelled token and the
    /// failure of the earliest declared faulted branch is rethrown. Caller cancellation always wins.
    /// </remarks>
    public Flow<TIn, TOut[]> FanOut(params Step<TOut, TOut>[] branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        var copy = (Step<TOut, TOut>[])branches.Clone();
        if (copy.Length == 0)
        {
            throw new ArgumentException("FanOut needs at least one branch.", nameof(branches));
        }

        if (Array.Exists(copy, static branch => branch is null))
        {
            throw new ArgumentException("FanOut branches must not be null.", nameof(branches));
        }

        var previous = _pipeline;
        var flowName = $"{Name} || {string.Join(" | ", copy.Select(static branch => branch.Name))}";
        return new Flow<TIn, TOut[]>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                var intermediate = await previous(input, context, cancellationToken).ConfigureAwait(false);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var tasks = new Task<TOut>[copy.Length];
                for (var index = 0; index < copy.Length; index++)
                {
                    tasks[index] = RunBranchAsync(copy[index], intermediate, context, linked);
                }

                try
                {
                    return await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (var task in tasks)
                    {
                        if (task.IsFaulted)
                        {
                            ExceptionDispatchInfo.Capture(task.Exception!.InnerExceptions[0]).Throw();
                        }
                    }

                    throw;
                }
            });

        static async Task<TOut> RunBranchAsync(
            Step<TOut, TOut> branch,
            TOut input,
            FlowContext context,
            CancellationTokenSource linked)
        {
            try
            {
                return await branch.RunAsync(input, context, linked.Token).ConfigureAwait(false);
            }
            catch
            {
                await linked.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>Retries the whole flow when it throws.</summary>
    /// <param name="maxAttempts">The maximum number of attempts, at least one.</param>
    /// <param name="delay">The delay between attempts.</param>
    /// <returns>A flow with retry semantics.</returns>
    public Flow<TIn, TOut> Retry(int maxAttempts, TimeSpan delay = default) =>
        Retry(maxAttempts, static _ => true, delay);

    /// <summary>Retries the whole flow when it throws and the predicate accepts the failure.</summary>
    /// <param name="maxAttempts">The maximum number of attempts, at least one.</param>
    /// <param name="shouldRetry">Decides whether a failure is retried; an exception thrown by the predicate fails the run.</param>
    /// <param name="delay">The delay between attempts.</param>
    /// <returns>A flow with retry semantics.</returns>
    public Flow<TIn, TOut> Retry(
        int maxAttempts,
        Func<Exception, bool> shouldRetry,
        TimeSpan delay = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(shouldRetry);
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
                    catch (Exception exception)
                    {
                        if (attempt >= maxAttempts || !shouldRetry(exception))
                        {
                            throw;
                        }

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

    /// <summary>Limits the whole flow to a deadline.</summary>
    /// <param name="timeout">The time budget; positive and finite.</param>
    /// <returns>A flow that throws <see cref="TimeoutException"/> when the deadline expires.</returns>
    /// <remarks>Caller cancellation always wins over the deadline.</remarks>
    public Flow<TIn, TOut> Timeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be positive and finite.");
        }

        var pipeline = _pipeline;
        var flowName = Name;
        return new Flow<TIn, TOut>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(timeout);
                try
                {
                    return await pipeline(input, context, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && linked.IsCancellationRequested)
                {
                    throw new TimeoutException($"The flow '{flowName}' timed out after {timeout}.");
                }
            });
    }

    /// <summary>Runs an alternative step with the original input when this flow fails.</summary>
    /// <param name="alternative">The recovery step.</param>
    /// <param name="when">Filters which failures trigger recovery; every failure does when omitted.</param>
    /// <returns>A flow that recovers or rethrows.</returns>
    /// <remarks>
    /// Caller cancellation never triggers the fallback. When both the flow and the alternative fail,
    /// the run throws an <see cref="AggregateException"/> with the primary failure first.
    /// </remarks>
    public Flow<TIn, TOut> Fallback(Step<TIn, TOut> alternative, Func<Exception, bool>? when = null)
    {
        ArgumentNullException.ThrowIfNull(alternative);
        var pipeline = _pipeline;
        var flowName = Name;
        return new Flow<TIn, TOut>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                try
                {
                    return await pipeline(input, context, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception primary)
                {
                    if (when is not null && !when(primary))
                    {
                        throw;
                    }

                    try
                    {
                        return await alternative.RunAsync(input, context, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception fallback)
                    {
                        throw new AggregateException(primary, fallback);
                    }
                }
            });
    }

    /// <summary>Governs the whole flow with hard limits and optional warn thresholds.</summary>
    /// <param name="budget">The budget; at least one hard limit, and every set value must be positive.</param>
    /// <returns>A flow that stops when a hard limit is crossed.</returns>
    /// <remarks>
    /// Wall time expires the segment like a deadline. Token and USD usage is charged explicitly by
    /// steps through <see cref="FlowContext.ChargeTokensAsync"/> and <see cref="FlowContext.ChargeUsdAsync"/>.
    /// Caller cancellation always wins over the wall deadline.
    /// </remarks>
    /// <exception cref="BudgetExceededException">Thrown when the flow crosses a hard limit.</exception>
    public Flow<TIn, TOut> Budget(Budget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ValidateBudget(budget);
        var pipeline = _pipeline;
        var flowName = Name;
        return new Flow<TIn, TOut>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var wall = budget.MaxWall;
                if (wall is { } maxWall)
                {
                    linked.CancelAfter(maxWall);
                }

                using var registration = BudgetRegistry.Push(new BudgetScope(budget, context));
                var warning = WarnWallAsync(budget, context, linked);
                try
                {
                    try
                    {
                        return await pipeline(input, context, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        wall is not null
                        && !cancellationToken.IsCancellationRequested
                        && linked.IsCancellationRequested)
                    {
                        var seconds = (decimal)wall.Value.TotalSeconds;
                        await context.Journal.AppendAsync(
                            new BudgetExceeded(
                                context.RunId,
                                BudgetDimension.Wall,
                                seconds,
                                seconds,
                                DateTimeOffset.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                        FlowMetrics.RecordBudgetExceeded(context.FlowName, BudgetDimension.Wall);
                        throw new BudgetExceededException(BudgetDimension.Wall, seconds, seconds);
                    }
                }
                finally
                {
                    linked.Cancel();
                    await warning.ConfigureAwait(false);
                }
            });
    }

    /// <summary>Requires a human decision before the whole flow runs.</summary>
    /// <param name="approver">The approver asked for a decision.</param>
    /// <param name="requirement">A short stable label such as <c>sandbox.execute</c>.</param>
    /// <param name="reason">Human-readable detail about why approval is needed.</param>
    /// <param name="timeout">The wait budget; positive and finite. Expiry denies by default.</param>
    /// <returns>A flow that runs only when the request is approved.</returns>
    /// <remarks>
    /// Wrapper order is explicit: <c>.RequireApproval(...).Budget(...)</c> counts the approval wait
    /// against <c>MaxWall</c>; <c>.Budget(...).RequireApproval(...)</c> does not.
    /// </remarks>
    /// <exception cref="ApprovalDeniedException">Thrown when the request is denied or times out.</exception>
    public Flow<TIn, TOut> RequireApproval(
        IApprover approver,
        string requirement,
        string reason,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(approver);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (timeout <= TimeSpan.Zero || timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be positive and finite.");
        }

        var pipeline = _pipeline;
        var flowName = Name;
        return new Flow<TIn, TOut>(
            flowName,
            async (input, context, cancellationToken) =>
            {
                var approvalId = ApprovalId.New();
                await context.Journal.AppendAsync(
                    new ApprovalRequested(
                        context.RunId,
                        approvalId,
                        requirement,
                        reason,
                        DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                ApprovalDecision decision;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linked.CancelAfter(timeout);
                    try
                    {
                        decision = await approver
                            .RequestAsync(new ApprovalRequest(approvalId, requirement, reason), linked.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        !cancellationToken.IsCancellationRequested && linked.IsCancellationRequested)
                    {
                        await context.Journal.AppendAsync(
                            new ApprovalResolved(
                                context.RunId,
                                approvalId,
                                ApprovalDecision.TimedOut,
                                DateTimeOffset.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                        FlowMetrics.RecordApproval(flowName, ApprovalDecision.TimedOut);
                        throw new ApprovalDeniedException(approvalId, ApprovalDecision.TimedOut);
                    }
                }

                if (decision != ApprovalDecision.Approved)
                {
                    var outcome = decision == ApprovalDecision.TimedOut
                        ? ApprovalDecision.TimedOut
                        : ApprovalDecision.Denied;
                    await context.Journal.AppendAsync(
                        new ApprovalResolved(context.RunId, approvalId, outcome, DateTimeOffset.UtcNow),
                        CancellationToken.None).ConfigureAwait(false);
                    FlowMetrics.RecordApproval(flowName, outcome);
                    throw new ApprovalDeniedException(approvalId, outcome);
                }

                await context.Journal.AppendAsync(
                    new ApprovalResolved(context.RunId, approvalId, ApprovalDecision.Approved, DateTimeOffset.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
                FlowMetrics.RecordApproval(flowName, ApprovalDecision.Approved);
                return await pipeline(input, context, cancellationToken).ConfigureAwait(false);
            });
    }

    private static void ValidateBudget(Budget budget)
    {
        if (budget.MaxTokens is null && budget.MaxUsd is null && budget.MaxWall is null)
        {
            throw new ArgumentException("A budget needs at least one hard limit.", nameof(budget));
        }

        if (budget.MaxTokens is { } maxTokens && maxTokens <= 0)
        {
            throw new ArgumentException("MaxTokens must be positive.", nameof(budget));
        }

        if (budget.MaxUsd is { } maxUsd && maxUsd <= 0)
        {
            throw new ArgumentException("MaxUsd must be positive.", nameof(budget));
        }

        if (budget.MaxWall is { } maxWall && (maxWall <= TimeSpan.Zero || maxWall == System.Threading.Timeout.InfiniteTimeSpan))
        {
            throw new ArgumentException("MaxWall must be positive and finite.", nameof(budget));
        }

        if (budget.WarnTokens is { } warnTokens)
        {
            if (warnTokens <= 0)
            {
                throw new ArgumentException("WarnTokens must be positive.", nameof(budget));
            }

            if (budget.MaxTokens is { } hardTokens && warnTokens >= hardTokens)
            {
                throw new ArgumentException("WarnTokens must be below MaxTokens.", nameof(budget));
            }
        }

        if (budget.WarnUsd is { } warnUsd)
        {
            if (warnUsd <= 0)
            {
                throw new ArgumentException("WarnUsd must be positive.", nameof(budget));
            }

            if (budget.MaxUsd is { } hardUsd && warnUsd >= hardUsd)
            {
                throw new ArgumentException("WarnUsd must be below MaxUsd.", nameof(budget));
            }
        }

        if (budget.WarnWall is { } warnWallSpan)
        {
            if (warnWallSpan <= TimeSpan.Zero || warnWallSpan == System.Threading.Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentException("WarnWall must be positive and finite.", nameof(budget));
            }

            if (budget.MaxWall is { } hardWall && warnWallSpan >= hardWall)
            {
                throw new ArgumentException("WarnWall must be below MaxWall.", nameof(budget));
            }
        }
    }

    private static async Task WarnWallAsync(Budget budget, FlowContext context, CancellationTokenSource linked)
    {
        if (budget.WarnWall is not { } warnWall)
        {
            return;
        }

        try
        {
            await Task.Delay(warnWall, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var seconds = (decimal)warnWall.TotalSeconds;
        try
        {
            await context.Journal.AppendAsync(
                new BudgetWarning(context.RunId, BudgetDimension.Wall, seconds, seconds, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            FlowMetrics.RecordBudgetWarning(context.FlowName, BudgetDimension.Wall);
        }
        catch (Exception)
        {
            // An advisory warning must not fail the run it warns about.
        }
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
        var context = new FlowContext(runId, journal) { FlowName = Name };
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
            activity?.SetTag(FlowDiagnostics.RunOutcomeTag, FlowMetrics.OkOutcome);
            FlowMetrics.RecordRun(Name, FlowMetrics.OkOutcome);
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            activity?.SetTag(FlowDiagnostics.RunOutcomeTag, ErrorCodes.FlowCancelled);
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowCancelled, "The run was cancelled.", DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            FlowMetrics.RecordRun(Name, ErrorCodes.FlowCancelled);
            throw;
        }
        catch (TimeoutException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(FlowDiagnostics.RunOutcomeTag, ErrorCodes.FlowTimedOut);
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowTimedOut, exception.Message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            FlowMetrics.RecordRun(Name, ErrorCodes.FlowTimedOut);
            throw;
        }
        catch (BudgetExceededException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(FlowDiagnostics.RunOutcomeTag, ErrorCodes.FlowBudgetExceeded);
            activity?.SetTag(FlowDiagnostics.BudgetDimensionTag, exception.Dimension.ToString());
            activity?.SetTag(FlowDiagnostics.BudgetLimitTag, (double)exception.Limit);
            activity?.SetTag(FlowDiagnostics.BudgetUsedTag, (double)exception.Used);
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowBudgetExceeded, exception.Message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            FlowMetrics.RecordRun(Name, ErrorCodes.FlowBudgetExceeded);
            throw;
        }
        catch (ApprovalDeniedException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(FlowDiagnostics.RunOutcomeTag, ErrorCodes.FlowApprovalDenied);
            activity?.SetTag(FlowDiagnostics.ApprovalIdTag, exception.ApprovalId.ToString());
            activity?.SetTag(FlowDiagnostics.ApprovalDecisionTag, exception.Decision.ToString());
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowApprovalDenied, exception.Message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            FlowMetrics.RecordRun(Name, ErrorCodes.FlowApprovalDenied);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(FlowDiagnostics.RunOutcomeTag, ErrorCodes.FlowStepFailed);
            await journal.AppendAsync(
                new RunFailed(runId, ErrorCodes.FlowStepFailed, exception.Message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            FlowMetrics.RecordRun(Name, ErrorCodes.FlowStepFailed);
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
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
