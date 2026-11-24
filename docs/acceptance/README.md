# Acceptance policy and records

This directory records how updates are accepted. It keeps the cadence honest without turning every small change into a meeting.

## Large updates

A large update requires explicit acceptance from the project owner before it is treated as complete. A large update includes any of the following:

- A public API change, including namespaces, types, signatures, defaults, or error codes.
- A security boundary, policy, isolation, network, filesystem, process, secret, or telemetry change.
- A release, package publication, signing change, or public announcement.
- A governance change, including the licence, CLA, code of conduct, security policy, or maintainer model.
- An architectural change recorded in a new ADR or a change to an accepted ADR.
- A destructive migration, data-format change, or compatibility break.
- A change to the acceptance or demo cadence itself.

The record must include the scope, evidence, risks, and the explicit acceptance decision.

## Small updates

A small update may be accepted autonomously after the evidence is recorded. A small update is limited to:

- Documentation, comments, and templates.
- Tests and fixtures that do not change the public contract.
- Internal refactoring with no public API or behavioural change.
- Build, formatting, and CI maintenance.
- Small bug fixes with a regression test.

Autonomous acceptance still requires:

- A clean build.
- Relevant tests passing.
- A demo or command transcript when behaviour is visible.
- No new secret, package, release, or public claim.
- A record in this directory or in the daily demo record.

## Records

Use `TEMPLATE.md` for each record. Name records `YYYY-MM-DD-short-name.md`. Link the demo evidence when one exists.

## Current records

- [2026-11-23 — Core skeleton](2026-11-23-core-skeleton.md)
- [2026-11-23 — CLA v1.0](2026-11-23-cla-v1.md)
- [2026-11-23 — Phase 1 and G2](2026-11-23-phase-1-g2.md)
- [2026-11-23 — MCP surface](2026-11-23-adr-0008-mcp.md)
- [2026-11-24 — Flow combinators](2026-11-24-adr-0011-flow-combinators.md)
- [2026-11-24 — Budgets and approvals](2026-11-24-adr-0012-budgets-and-approvals.md)
- [2026-11-24 — Observability](2026-11-24-adr-0013-observability.md)
