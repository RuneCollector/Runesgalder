# ADR-0011: Flow combinators for branching, fan-out, timeouts, and fallbacks

- Status: Accepted
- Date: 2026-11-24
- Accepted: 2026-11-24
- Deciders: karlmoriaty

## Context

The v0.1 plan budgets exactly seven Flow combinators: `Then`, `Branch`, `FanOut`/`FanIn`, `Retry`, `Timeout`, `Fallback`, and `Checkpoint`. `Then`, `Retry`, and `Checkpoint` exist. `Branch`, `FanOut`/`FanIn`, `Timeout`, and `Fallback` do not, so multi-agent patterns cannot yet be expressed with the agreed set.

The combinators are public API, so their shape and failure semantics need a decision before implementation. The journal schema (ADR-0007) already carries run and step lifecycle events; adding event kinds would be a schema change. The v0.1 target avoids that unless the value is clear.

## Decision

### Shape

All combinators are instance methods on `Flow<TIn, TOut>` or `Step<TIn, TOut>`, return the same fluent types, and do not introduce new parameters to `RunAsync` or `BeginRun`.

- `Branch` chooses one of two steps from the flow output.
- `FanOut` runs steps concurrently over the same output; `FanIn` is the following `Then` step that consumes the ordered array.
- `Timeout` applies to the whole flow segment built so far (`Flow.Timeout`) or to a single step (`Step.Timeout`).
- `Fallback` runs an alternative step from the original flow input when the primary flow fails.
- `Retry` gains an exception predicate; the existing overload keeps current behaviour.

### Branch

```csharp
public Flow<TIn, TNext> Branch<TNext>(
    Func<TOut, bool> predicate,
    Step<TOut, TNext> whenTrue,
    Step<TOut, TNext> whenFalse)
```

- The predicate runs after the prefix produces `TOut`; the chosen step then runs with that output.
- Both branches must produce the same `TNext`, keeping the flow typed end to end.
- Only the chosen step emits `StepStarted` and its terminal event. The flow name records both branch names.
- A throwing predicate fails the run like a step failure; no branch runs.
- Branching on errors is `Fallback` or `Retry`, not `Branch`. Branching on more than two cases composes nested `Branch` calls.

### FanOut and FanIn

```csharp
public Flow<TIn, TOut[]> FanOut(params Step<TOut, TOut>[] branches)
```

- Every branch receives the same `TOut` and runs concurrently. Branches must treat the input as read-only; no copy is made.
- Results keep declaration order regardless of completion order.
- At least one branch is required; an empty call throws.
- The first branch failure cancels the remaining branches cooperatively, waits for all of them to settle, then rethrows the first failure by declaration order. Outer cancellation cancels every branch and rethrows as cancellation.
- The journal interleaves the branches' step events; append order is the observation order, not a schedule guarantee. Append implementations are already safe for concurrent writers within one process.
- `FanIn` is the next `Then` step, which takes `TOut[]` and returns whatever the flow needs. A separate `FanIn` method would only alias `Then`.
- Heterogeneous branch outputs (tuples or a discriminated result) are deferred; v0.1 fans out to the same output type.

### Timeout

```csharp
public Flow<TIn, TOut> Timeout(TimeSpan timeout)
public Step<TIn, TOut> Timeout(TimeSpan timeout)
```

- Both use a linked cancellation source with `CancelAfter(timeout)`. Non-positive and infinite values are rejected.
- On expiry the combinator throws `TimeoutException` naming the flow or step. A step timeout calls the inner step body directly under the linked token, so the journal still shows one step, not a wrapper.
- Caller cancellation wins: if the outer token is cancelled, `OperationCanceledException` propagates unchanged and no timeout is reported.
- A step interrupted by a flow timeout sees its linked token cancelled and may journal `flow.cancelled`; the run's terminal event carries the authoritative `flow.timed_out`. Nested timeouts use the same rule: the first linked source to fire decides the exception.
- A new stable code `flow.timed_out` is added to `ErrorCodes`. `Step.RunAsync` and flow completion map `TimeoutException` to it. The journal schema version does not change because no event kind is added.

### Fallback

```csharp
public Flow<TIn, TOut> Fallback(
    Step<TIn, TOut> alternative,
    Func<Exception, bool>? when = null)
```

- The primary flow runs first. When it throws and `when` accepts the exception (default: all exceptions), the alternative step runs from the original `TIn`.
- Caller cancellation never triggers the fallback.
- The fallback step emits its own lifecycle events. If it succeeds, the run succeeds.
- If the fallback also fails, the run throws `AggregateException` with the primary exception first and the fallback exception second; the journal already holds both step failures.
- The predicate runs on the primary exception only; a throwing predicate fails the run.

### Retry predicate

```csharp
public Flow<TIn, TOut> Retry(
    int maxAttempts,
    Func<Exception, bool> shouldRetry,
    TimeSpan delay = default)
```

- The predicate decides whether a failed attempt is retried. A false result rethrows the original exception with no `StepRetryScheduled` event.
- A throwing predicate fails the run. Caller cancellation is never retried.
- `Retry(maxAttempts, delay)` remains and means `shouldRetry: static _ => true`, so existing runs and tests keep their behaviour.
- Retrying on a result value is expressed as a validating step that throws when the result is unacceptable; v0.1 adds no result-predicate overload.

### Events and tracing

- No new journal event kinds. Branch, fan-out, timeout, and fallback decisions are visible through flow names, step events, and error codes.
- Step and run activities keep their existing names and tags. `FanOut` produces sibling step activities under the flow activity; their start order is concurrent by design.

### Testing

- Branch: both paths, predicate failure, cancellation before the predicate, typed output.
- FanOut: concurrency (branches overlap), declaration-order results, sibling cancellation on failure, outer cancellation, empty argument.
- Timeout: flow and step timeouts, caller cancellation precedence, non-positive values, `flow.timed_out` in the run journal, single step event set for `Step.Timeout`.
- Fallback: recovery, predicate filtering, cancellation bypass, both-fail aggregate.
- Retry: matching and non-matching predicates, predicate failure, unchanged default overload.

## Consequences

Positive:

- The seven-combinator budget is complete without new event kinds or journal migration.
- Fan-out reuses `Then` for aggregation, so there is one way to combine results.
- Timeout and fallback compose with `Retry` and `Checkpoint` in the same fluent style.

Negative:

- `FanOut` is homogeneous; mixed outputs need nested arrays or later additions.
- Step-level timeout bypasses the inner step's own activity and event wrapping to avoid duplicate steps; the timeout belongs to the wrapper's execution of the same step name.
- A flow timeout can journal `flow.cancelled` for a step while the run reports `flow.timed_out`; readers must treat the run terminal event as authoritative.
- Fallback failure surfaces as `AggregateException`, which callers must unwrap when they need the original type.
- Retry remains exception-driven; result-based retries cost an extra validation step.

## Alternatives considered

- **Branch over two flows instead of steps.** Rejected: flows own run boundaries and would emit nested `RunStarted` events or need internal pipeline access. Steps compose without either.
- **FanOut with tuple overloads for two or three heterogeneous outputs.** Rejected for v0.1: overload multiplication, awkward aggregation, and the common agent pattern produces the same output type.
- **A separate `FanIn` method.** Rejected: it would alias `Then` and add no semantics.
- **Timeout implemented as a wrapper step for flows.** Rejected: it would double step events and rename the flow. Flow-level timeout wraps the segment; step-level timeout calls the step body directly.
- **A `flow.timeout` event kind.** Rejected: it would bump the journal schema for information already carried by `RunFailed` and `StepFailed`.
- **Result-predicate retry.** Deferred; a validating step keeps retry semantics simple and typed.
- **Cancelling a fan-out branch only through the outer token.** Rejected: a failing branch should stop wasted sibling work promptly.

## Verification plan

- Unit tests in `Runesgalder.Flow.Tests` cover the cases above with an in-memory journal and gated tasks for concurrency.
- A demo updates `docs/demos/` with a fan-out plus fallback flow and the journal transcript.
- Build and tests pass on Windows locally and on both CI lanes; formatting stays clean.