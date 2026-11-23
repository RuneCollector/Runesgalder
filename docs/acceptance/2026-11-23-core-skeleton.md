# Acceptance — 2026-11-23 — Core skeleton

- Type: small
- Decision: accepted autonomously
- Decision owner: maintainer
- Related demo: [2026-11-23](../demos/2026-11-23.md)

## Scope

- Typed Flow composition (`Then`), retry, cancellation, and in-memory journal.
- Sandbox policy contracts and path validation.
- CLI `demo` and `doctor`.
- Solution, build configuration, tests, and initial documentation.
- Governance, security, and ADR drafts.

## Evidence

- `dotnet build Runesgalder.sln -c Release`: 0 warnings, 0 errors.
- `dotnet test Runesgalder.sln -c Release --no-build`: 6 passed.
- CLI `demo`: result `42`, journal events emitted in the expected order.
- CLI `doctor`: reports .NET 10.0.12, `win-x64`, not a container, and “Sandbox host: not Linux”.
- No package, release, or public announcement.
- No secret used in the demo.

## Risks

- The sandbox engine is not implemented. No isolation claim is made.
- The Flow API is a skeleton and will grow. The namespace change to `Runesgalder` is intentional and must be kept stable.
- CI and secret scanning are not yet in place; they are required before the first push.

## Follow-ups

- Add CI, CodeQL, Dependabot, issue/PR templates, and CODEOWNERS.
- Run formatting and secret scanning before the first push.
- Send the CLA draft for review before enforcing it.
- Build the Roslyn pipeline, Linux worker, and data-analysis demo.

## Decision notes

This is a small, reversible update with passing evidence and no security claim. It is accepted so the repository can be pushed and reviewed. Large updates, releases, and security claims still require explicit owner acceptance.
