# Budgets and approvals acceptance

- Date: 2026-11-24
- Update: ADR-0012 Budgets and approvals — shared budget and approval types, four journal events, the `Budget` and `RequireApproval` combinators, and explicit usage charging
- Type: large
- Decision: accepted
- Decision owner: karlmoriaty
- Related demo: docs/demos/2026-11-24.md
- Related issue or PR: `docs/adr/0012-budgets-and-approvals.md`

## Scope

- Shared types in `Runesgalder.Abstractions`: `BudgetDimension`, `Budget`, `ApprovalId`, `ApprovalRequest`, `ApprovalDecision`, `IApprover`, `BudgetExceededException`, and `ApprovalDeniedException`. Sandbox will consume the same vocabulary.
- Budget validation runs when a budget is attached: at least one hard limit, every set value positive, warn thresholds below their hard limits, positive finite wall values.
- Four journalled events `BudgetWarning`, `BudgetExceeded`, `ApprovalRequested`, and `ApprovalResolved` with codec tags `budget.warning`, `budget.exceeded`, `approval.requested`, and `approval.resolved`; enums serialise as strings via `JsonStringEnumConverter`; wall values are reported in seconds; the SQLite schema is unchanged.
- `Flow.Budget(Budget)` governs the segment built so far. Wall uses a linked token with `CancelAfter(MaxWall)` and throws `BudgetExceededException` on expiry; caller cancellation stays `flow.cancelled`. Token and USD usage is charged explicitly through `FlowContext.ChargeTokensAsync` and `ChargeUsdAsync`, which update every active scope innermost first, warn once per dimension per scope, and throw at a hard crossing.
- `Flow.RequireApproval(approver, requirement, reason, timeout)` creates a fresh `ApprovalId` per entry, journals the request, awaits the approver under a linked timeout, denies on denial or timeout with `ApprovalDeniedException`, propagates approver exceptions unchanged, and writes no resolution on caller cancellation. Wrapper order decides whether the wait counts against `MaxWall`.
- New stable error codes `flow.budget_exceeded` and `flow.approval_denied` mapped in `Step.RunAsync` and `Flow.RunCoreAsync` terminal events.

## Evidence

- Build: succeeded with 0 warnings and 0 errors; `dotnet format --verify-no-changes` clean for the solution.
- Tests: Flow suite 85 passed (63 before) — new coverage for wall expiry, token and USD hard limits, one warning per dimension per scope, wall warn without failure, no-budget no-ops, nested scopes, inner scope lifetime, caller cancellation precedence, invalid budgets, negative charges, approved, denied, timeout, approver-returned timeout, caller cancellation without a resolution, approver exceptions, invalid approval arguments, and round-trips of the four new events through `SqliteJournal`.
- Full solution: Flow 85 passed; MCP 61 passed with 3 Docker-only skipped; Sandbox 45 passed with 5 Docker-only skipped; Demo 5 passed.
- Demo: `docs/demos/2026-11-24.md` records the budgeted, approved flow and its journal transcript.
- Security review: no new dependencies; no isolation, network, filesystem, secret, or telemetry surface changed. The approval timeout and budget wall are cooperative cancellations over existing tokens.
- Secret scan: the change contains no credentials, tokens, or machine-specific paths.

## Risks

- Token and USD totals depend on host adapters calling the charge methods with correct values; the runtime never infers cost from `ModelCallCompleted`.
- Budget scopes are tracked per asynchronous execution context, so a sibling fan-out branch does not see a budget attached inside another branch; enclosing budgets flow into all branches.
- A wall warn timer appends one advisory event and swallow journal failures by design; a failed advisory must not fail the run it warns about.
- A cooperative approver is required: an approver that ignores its cancellation token can hold the run past the timeout.
- The first tripped scope decides the failure; outer scopes may not record their own exceed event for the same charge.

## Follow-ups

- Add the OTel exporter ADR; budget and approval attributes join the trace when it lands.
- Document budgets and approvals in the README governance section before the v0.1-preview gate.
- Extend the sandbox runner to adopt the same `Budget` and `IApprover` types.
- Re-run the full suite on Ubuntu and Windows CI for the implementing commit.

## Decision notes

- Accepted in chat on 2026-11-24 with the recommended option: budgets and approvals implemented together under one ADR because they share the audit surface and wrapper mechanics. They can split later if either grows.

## Implementation evidence (same day)

- Shared types, the four events, codec tags, and the string-enum converter added; `BudgetScope` with an `AsyncLocal` registry keeps management scope-correct across `FanOut` branches.
- `Flow.Budget`, `Flow.RequireApproval`, `FlowContext.ChargeTokensAsync`, and `FlowContext.ChargeUsdAsync` implemented; `Step.RunAsync` and `Flow.RunCoreAsync` map both new exceptions.
- Flow tests: 85 passed (previously 63).