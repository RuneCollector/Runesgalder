# Flow combinators acceptance

- Date: 2026-11-24
- Update: ADR-0011 Flow combinators — Branch, FanOut with an ordered FanIn, Flow and Step deadlines, Fallback, and a Retry predicate
- Type: large
- Decision: accepted
- Decision owner: karlmoriaty
- Related demo: docs/demos/2026-11-24.md
- Related issue or PR: `docs/adr/0011-flow-combinators.md`

## Scope

- `Branch<TNext>(predicate, whenTrue, whenFalse)` on `Flow<TIn, TOut>`; both branches share `TNext`, only the chosen step emits step events, and the flow name records both branch names.
- `FanOut(params Step<TOut, TOut>[] branches)` returning `Flow<TIn, TOut[]>`: branches run concurrently, results keep declaration order, the first failure cancels siblings and the earliest declared faulted branch is rethrown, and caller cancellation always wins. `FanIn` remains the following `Then` step that consumes the ordered array.
- `Flow.Timeout` and `Step.Timeout`: positive finite deadlines backed by a linked token and `CancelAfter`; expiry throws `TimeoutException` naming the flow or step; caller cancellation wins; `Step.Timeout` wraps `ExecuteAsync` so a step still emits exactly one event pair.
- `Fallback(alternative, when = null)` runs the alternative with the original flow input; caller cancellation never triggers it; both failures surface as `AggregateException(primary, fallback)`; a throwing filter fails the run.
- `Retry(maxAttempts, shouldRetry, delay = default)`: the predicate gates retries; non-matching failures rethrow without a retry event; a throwing predicate fails the run; the existing overload is exactly `static _ => true`.
- New stable error code `ErrorCodes.FlowTimedOut` (`flow.timed_out`) mapped in step and run terminal events. No new journal event kinds; the journal schema is unchanged.

## Evidence

- Build: succeeded with 0 warnings and 0 errors; `dotnet format --verify-no-changes` clean for the Abstractions, Flow, and Flow test projects.
- Tests: Flow suite 63 passed — 23 new tests cover both branches, predicate failure, cancellation before branching, concurrent fan-out with declaration-order results, sibling cancellation, earliest-declared failure, caller cancellation, empty and null branch lists, flow and step deadlines, caller cancellation precedence, deadline validation, fallback recovery, filtering, cancellation bypass, both-fail aggregation, throwing filters, predicate retries, non-matching rethrow, and cancellation never being retried.
- Demo: `docs/demos/2026-11-24.md` records the combinator suite and transcript summary.
- Security review: no new dependencies; no effect, policy, isolation, network, filesystem, or secret surface changed. The combinators only orchestrate existing steps and journal events.
- Secret scan: the change contains no credentials, tokens, or machine-specific paths.

## Risks

- Concurrent branches share one journal and checkpoint store; both are append-safe within a process, and checkpoint writes stay first-write-wins.
- Branch names should be unique within a `FanOut` for readable journals; this is not enforced.
- Deadlines are cooperative: a step that ignores its cancellation token can still overrun.
- `Fallback` hides the primary failure unless both fail; callers that must observe it should inspect the journal.

## Follow-ups

- Add budget, approval, and OTel exporter combinators under new ADRs when Phase 2 reaches them.
- Document the combinator set in the README feature list before the v0.1-preview gate.
- Re-run the full suite on Ubuntu and Windows CI for the implementing commit.

## Decision notes

- Accepted in chat on 2026-11-24 with the recommended option: the seven-combinator v0.1 set completed, no new journal event kinds, and `flow.timed_out` added to the stable error codes.

## Implementation evidence (same day)

- `Branch`, `FanOut`, `Timeout`, and `Fallback` implemented on `Flow<TIn, TOut>`; `Step.Timeout` implemented as a same-name wrapper that calls `ExecuteAsync` directly; `Retry` gained the predicate overload with the old overload delegating to it.
- `ErrorCodes.FlowTimedOut` added; `Step.RunAsync` and `Flow.RunCoreAsync` map `TimeoutException` before the general failure catch.
- Flow tests: 63 passed (previously 40).