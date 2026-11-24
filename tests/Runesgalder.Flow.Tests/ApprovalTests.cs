namespace Runesgalder.FlowTests;

public sealed class ApprovalTests
{
    [Fact]
    public async Task ApprovedRunsTheSegment()
    {
        var approver = new ScriptedApprover(
            static (_, _) => ValueTask.FromResult(ApprovalDecision.Approved));
        var calls = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "work",
                value =>
                {
                    calls++;
                    return value + 1;
                }))
            .RequireApproval(approver, "test.run", "Unit test approval", TimeSpan.FromSeconds(5));
        var journal = new InMemoryJournal();

        Assert.Equal(2, await flow.RunAsync(1, journal));

        Assert.Equal(1, calls);
        var request = Assert.Single(approver.Requests);
        Assert.Equal("test.run", request.Requirement);
        Assert.Equal("Unit test approval", request.Reason);
        var events = journal.Snapshot();
        Assert.Collection(
            events,
            e => Assert.IsType<RunStarted>(e),
            e => Assert.Equal(request.Id, Assert.IsType<ApprovalRequested>(e).ApprovalId),
            e => Assert.Equal(ApprovalDecision.Approved, Assert.IsType<ApprovalResolved>(e).Decision),
            e => Assert.Equal("work", Assert.IsType<StepStarted>(e).StepName),
            e => Assert.Equal("work", Assert.IsType<StepCompleted>(e).StepName),
            e => Assert.IsType<RunCompleted>(e));
    }

    [Fact]
    public async Task DeniedFailsWithTheStableCode()
    {
        var approver = new ScriptedApprover(
            static (_, _) => ValueTask.FromResult(ApprovalDecision.Denied));
        var calls = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "work",
                value =>
                {
                    calls++;
                    return value;
                }))
            .RequireApproval(approver, "test.run", "Unit test approval", TimeSpan.FromSeconds(5));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<ApprovalDeniedException>(() => flow.RunAsync(1, journal));

        Assert.Equal(ApprovalDecision.Denied, exception.Decision);
        Assert.Equal(approver.Requests[0].Id, exception.ApprovalId);
        Assert.Equal(0, calls);
        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is ApprovalResolved resolved && resolved.Decision == ApprovalDecision.Denied);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowApprovalDenied);
        Assert.DoesNotContain(events, runEvent => runEvent is StepStarted);
    }

    [Fact]
    public async Task TimeoutResolvesTimedOutAndDenies()
    {
        var approver = new ScriptedApprover(
            static async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ApprovalDecision.Denied;
            });
        var calls = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "work",
                value =>
                {
                    calls++;
                    return value;
                }))
            .RequireApproval(approver, "test.run", "Unit test approval", TimeSpan.FromMilliseconds(50));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<ApprovalDeniedException>(() => flow.RunAsync(1, journal));

        Assert.Equal(ApprovalDecision.TimedOut, exception.Decision);
        Assert.Equal(0, calls);
        var events = journal.Snapshot();
        var resolved = Assert.Single(events.OfType<ApprovalResolved>());
        Assert.Equal(ApprovalDecision.TimedOut, resolved.Decision);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowApprovalDenied);
    }

    [Fact]
    public async Task ApproverReturningTimedOutIsDenied()
    {
        var approver = new ScriptedApprover(
            static (_, _) => ValueTask.FromResult(ApprovalDecision.TimedOut));
        var flow = Flow.Start(Step.Define<int, int>("work", value => value))
            .RequireApproval(approver, "test.run", "Unit test approval", TimeSpan.FromSeconds(5));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<ApprovalDeniedException>(() => flow.RunAsync(1, journal));

        Assert.Equal(ApprovalDecision.TimedOut, exception.Decision);
        Assert.Contains(
            journal.Snapshot(),
            runEvent => runEvent is ApprovalResolved resolved && resolved.Decision == ApprovalDecision.TimedOut);
    }

    [Fact]
    public async Task CallerCancellationWritesNoResolution()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approver = new ScriptedApprover(
            async (_, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ApprovalDecision.Approved;
            });
        var flow = Flow.Start(Step.Define<int, int>("work", value => value))
            .RequireApproval(approver, "test.run", "Unit test approval", TimeSpan.FromSeconds(30));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(1, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is ApprovalRequested);
        Assert.DoesNotContain(events, runEvent => runEvent is ApprovalResolved);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
    }

    [Fact]
    public async Task ApproverExceptionPropagates()
    {
        var approver = new ScriptedApprover(
            static (_, _) => throw new InvalidOperationException("approver broke"));
        var calls = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "work",
                value =>
                {
                    calls++;
                    return value;
                }))
            .RequireApproval(approver, "test.run", "Unit test approval", TimeSpan.FromSeconds(5));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => flow.RunAsync(1, journal));

        Assert.Equal("approver broke", exception.Message);
        Assert.Equal(0, calls);
        var events = journal.Snapshot();
        Assert.DoesNotContain(events, runEvent => runEvent is ApprovalResolved);
        Assert.DoesNotContain(events, runEvent => runEvent is StepStarted);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowStepFailed);
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value));
        var approver = new ScriptedApprover(
            static (_, _) => ValueTask.FromResult(ApprovalDecision.Approved));

        Assert.Throws<ArgumentNullException>(() => flow.RequireApproval(null!, "r", "why", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => flow.RequireApproval(approver, " ", "why", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => flow.RequireApproval(approver, "r", " ", TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.RequireApproval(approver, "r", "why", TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.RequireApproval(approver, "r", "why", TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.RequireApproval(approver, "r", "why", Timeout.InfiniteTimeSpan));
    }

    private sealed class ScriptedApprover(
        Func<ApprovalRequest, CancellationToken, ValueTask<ApprovalDecision>> respond) : IApprover
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public ValueTask<ApprovalDecision> RequestAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }
}
