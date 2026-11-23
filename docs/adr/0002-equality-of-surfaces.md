# ADR-0002: Equality of Flow and Sandbox

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

The project has two product pillars. Treating them as equal could mean equal feature count, equal engineering effort, equal security investment, or equal product status. Those are different goals and they conflict.

Flow competes in a crowded orchestration market. Sandbox addresses a genuine gap in safe .NET code execution. The sandbox needs more security investment than a feature-count comparison would suggest.

## Decision

Equality means equal product status, API quality, and release cadence. It does not mean equal feature count or equal security investment.

Every release must show both surfaces:

- Flow has a coherent, tested API and a visible slice of orchestration capability.
- Sandbox has a coherent, tested API and an honest isolation claim.
- The integration demo exercises both surfaces together.

The working budget is approximately 35% Flow, 50% Sandbox, and 15% shared runtime, documentation, and release work. The budget can change with evidence, but the release rule does not.

Security investment follows risk, not symmetry. A sandbox feature may take longer than a Flow feature of similar user-visible size.

## Consequences

Positive:

- The sandbox can receive the security work it needs without being judged as a feature-count peer.
- Flow can stay small and focused instead of chasing orchestration frameworks with much larger scopes.
- Every release tells a complete story.

Negative:

- The two lines will not look symmetrical in a feature matrix.
- The team must resist pressure to add sandbox features before the isolation model is credible.
- Planning must reserve shared work for integration and documentation.

## Alternatives considered

- **Full symmetry.** Rejected: it would either underfund sandbox security or overbuild Flow.
- **Sandbox as a feature of Flow.** Rejected: it weakens the sandbox as a standalone product and as an MCP-facing capability.
- **Sequential delivery: Flow first, Sandbox later.** Rejected: the killer demo and the product differentiation require both from the start.
