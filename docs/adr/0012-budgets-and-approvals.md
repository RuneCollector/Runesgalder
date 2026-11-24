# ADR-0012: Budgets and approvals for v0.1

- Status: Accepted
- Date: 2026-11-24
- Accepted: 2026-11-24
- Deciders: karlmoriaty

## Context

Phase 2 requires budgets and approvals as v0.1 P0 governance items. The plan fixes the budget dimensions (tokens, USD, wall), the soft-warn and hard-stop behaviour, the approval rules (approve or deny, timeout denies by default, every decision is journalled), and the event kinds `BudgetWarning`, `BudgetExceeded`, `ApprovalRequested`, and `ApprovalResolved`.

The shared runtime owns Effect, Policy, Budget, Approval, Journal, and RunEvent semantics so Flow and Sandbox can share one governance story. Policy already answers "may this effect run"; budgets accumulate cost and approvals record a human decision on top of that. Neither exists in code today.

Two constraints shape the decision. Prices vary by provider and are host configuration, so the runtime must not embed a vendor price table. Telemetry stays off by default, so the journal is the audit surface and the OTel exporter remains a separate later decision.

## Decision

### Shared types

Budgets and approvals live in `Runesgalder.Abstractions` because Sandbox will consume the same types.

```csharp
public enum BudgetDimension
{
    Tokens,
    Usd,
    Wall,
}

public sealed record Budget
{
    public long? MaxTokens { get; init; }
    public decimal? MaxUsd { get; init; }
    public TimeSpan? MaxWall { get; init; }
    public long? WarnTokens { get; init; }
    public decimal? WarnUsd { get; init; }
    public TimeSpan? WarnWall { get; init; }
}
```

- At least one hard limit (`Max*`) is required. Every set value must be positive.
- A warn limit is optional, applies with or without the matching hard limit, and must be below the hard limit when both are set.
- Wall is wall-clock time of the governed segment. Tokens and USD are charged explicitly; the runtime never infers cost from events.
- Validation runs when a budget is attached, not in the record, so stored payloads stay deserialisable.

```csharp
public readonly record struct ApprovalId(Guid Value)
{
    public static ApprovalId New() => new(Guid.NewGuid());
}

public sealed record ApprovalRequest(ApprovalId Id, string Requirement, string Reason);

public enum ApprovalDecision
{
    Approved,
    Denied,
    TimedOut,
}

public interface IApprover
{
    ValueTask<ApprovalDecision> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);
}
```

- Approvers return `Approved` or `Denied`. `TimedOut` is produced only by the runtime.
- `Requirement` is a short stable label such as `sandbox.execute`; `Reason` is human-readable detail.
- The library never approves implicitly. A demo or test may supply a scripted approver, but it must be passed explicitly.

Two public exception types carry the failure to callers:

```csharp
public sealed class BudgetExceededException : Exception
{
    public BudgetDimension Dimension { get; }
    public decimal Limit { get; }
    public decimal Used { get; }
}

public sealed class ApprovalDeniedException : Exception
{
    public ApprovalId ApprovalId { get; }
    public ApprovalDecision Decision { get; }
}
```

### Journal events

```csharp
public sealed record BudgetWarning(
    RunId RunId,
    BudgetDimension Dimension,
    decimal Limit,
    decimal Used,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

public sealed record BudgetExceeded(
    RunId RunId,
    BudgetDimension Dimension,
    decimal Limit,
    decimal Used,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

public sealed record ApprovalRequested(
    RunId RunId,
    ApprovalId ApprovalId,
    string Requirement,
    string Reason,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);

public sealed record ApprovalResolved(
    RunId RunId,
    ApprovalId ApprovalId,
    ApprovalDecision Decision,
    DateTimeOffset Timestamp) : RunEvent(RunId, Timestamp);
```

- Wall values are reported in seconds.
- Codec tags are `budget.warning`, `budget.exceeded`, `approval.requested`, and `approval.resolved`.
- Enums serialise as strings by adding `JsonStringEnumConverter` to the shared JSON options. Existing payloads contain no enum properties, so stored journals stay readable.
- The journal tables do not change. Event kinds are additive; older readers still reject unknown tags, which is the existing contract.

### Budget combinator

```csharp
public Flow<TIn, TOut> Budget(Budget budget)
```

- The budget governs the segment built so far, the same scoping rule as `Timeout` and `Fallback`.
- Wall: a linked cancellation source starts `CancelAfter(MaxWall)` when the segment begins. Expiry appends `BudgetExceeded` for `Wall` and throws `BudgetExceededException`. Caller cancellation wins and stays `flow.cancelled`.
- Tokens and USD: steps charge usage through two new `FlowContext` methods.

```csharp
public ValueTask ChargeTokensAsync(long tokens, CancellationToken cancellationToken = default);
public ValueTask ChargeUsdAsync(decimal usd, CancellationToken cancellationToken = default);
```

- Each charge updates every active budget scope, innermost first. With no scope active, both methods are cheap no-ops.
- A charge that crosses a warn threshold appends one `BudgetWarning` per scope per dimension; later charges do not warn again.
- A charge that crosses a hard limit appends `BudgetExceeded` and throws immediately, so the enclosing step journals `StepFailed` and the run journals `RunFailed`, both with `flow.budget_exceeded`.
- A wall warn timer appends `BudgetWarning` at `WarnWall` and is disposed when the segment ends.
- Charges are explicit because USD needs a host price table. `ModelCallCompleted` remains informational and is never converted into budget usage by the runtime.

### Approval combinator

```csharp
public Flow<TIn, TOut> RequireApproval(
    IApprover approver,
    string requirement,
    string reason,
    TimeSpan timeout)
```

- Before the segment runs, a fresh `ApprovalId` is created and `ApprovalRequested` is appended. The approver is awaited under a linked token with `CancelAfter(timeout)`; non-positive and infinite timeouts are rejected.
- `Approved` appends `ApprovalResolved(Approved)` and the segment runs.
- `Denied` appends `ApprovalResolved(Denied)` and throws `ApprovalDeniedException`; the run fails with `flow.approval_denied`.
- Timeout appends `ApprovalResolved(TimedOut)` and fails the same way; timeouts deny by default.
- Caller cancellation cancels the approver call and the run, writes no resolution event, and the run records `flow.cancelled`.
- An approver exception propagates unchanged and fails the run.
- Wrapper order is explicit: `.RequireApproval(...).Budget(...)` makes the approval wait count against `MaxWall`; the reverse order does not.

### Error codes and events in the runtime

- `ErrorCodes` gains `flow.budget_exceeded` and `flow.approval_denied`.
- `Step.RunAsync` and flow completion map `BudgetExceededException` and `ApprovalDeniedException` to those codes, alongside the existing timeout mapping.
- Step and run activity names and tags do not change. Budget and approval attributes join the OTel work when that ADR lands; the journal is the audit surface for v0.1.

### Testing

- Budget: wall expiry journals `BudgetExceeded` and fails with `flow.budget_exceeded`; token and USD hard limits fail at the charge; one `BudgetWarning` per dimension per scope; charges without a budget are no-ops; nested scopes charge every active budget; caller cancellation precedence; invalid budgets are rejected.
- Approval: approved runs the segment; denied fails with both events and the stable code; timeout resolves `TimedOut` and denies; caller cancellation writes no resolution; approver exceptions propagate; invalid timeouts are rejected.
- Journals: the four new events round-trip through `SqliteJournal`, including string enums.

## Consequences

Positive:

- Every budget and approval decision is auditable in the journal with stable codes.
- Flow and Sandbox share one governance vocabulary; the sandbox runner can adopt the same types later.
- No journal migration and no new dependency.

Negative:

- Token and USD accounting depends on host adapters calling the charge methods correctly; the runtime cannot verify usage on its own.
- Approvals require a host-implemented `IApprover`; there is no built-in interactive prompt in v0.1.
- Wrapper order between approvals, budgets, and timeouts changes what each governs, so documentation must stay precise.
- Charges write journal entries inside steps, which adds journal traffic on long runs.
- `AggregateException`-style nesting is not used here; the first tripped scope decides the failure, and outer scopes may not record their own exceed event.

## Alternatives considered

- **Budget and timeout as `RunAsync` options.** Rejected: it changes the `RunAsync` signature and loses segment scoping; the fluent wrappers compose.
- **Automatic token accounting from `ModelCallCompleted`.** Rejected: tokens could be summed, but USD needs a price table the library must not embed, and two accounting paths would diverge.
- **Budget as an `IEffectPolicy`.** Rejected: policy answers allow or deny per effect; budgets accumulate across effects and need warn thresholds.
- **Approval as an effect policy decision.** Rejected: policy is automatic and declarative; approvals record a human decision and its timeout.
- **Auto-approve as the library default.** Rejected by the plan; demos must opt in explicitly.
- **Two enums for approver input and journalled outcome.** Rejected for v0.1: one enum keeps the event and the contract aligned; the runtime alone produces `TimedOut`.
- **Separate ADRs for budgets and approvals.** Deferred: they share the audit surface and the same wrapper mechanics, so one review keeps them consistent. They can split later if either grows.

## Verification plan

- Unit tests in `Runesgalder.Flow.Tests` use a scripted approver, the in-memory journal, and short real deadlines, following the timeout tests.
- Journal round-trip tests extend the existing `SqliteJournal` cases.
- A same-day demo records a budgeted, approved flow with its journal transcript.
- Build, tests, and formatting stay clean on both CI lanes.