# ADR-0004: Isolation tiers and conservative claims

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

No single isolation mechanism is appropriate for every trust level. A local developer running their own code has different needs from a service executing code generated from untrusted prompt input. Claiming "secure sandbox" without naming the boundary invites over-trust.

The project needs a staged path that delivers a useful v0.1 without pretending to solve multi-tenant hostile isolation.

## Decision

Runesgalder uses explicit isolation tiers:

1. **Trusted development mode.** No strong isolation claim. For local experiments only.
2. **Linux container plus process hardening.** The v0.1 default. Rootless container, user namespaces, cgroups v2, seccomp, Landlock where available, read-only root filesystem, no Docker socket, no-new-privileges, dropped capabilities, no network by default.
3. **gVisor.** Optional stronger userspace kernel boundary, targeted for v0.5 evaluation.
4. **WASM/WASI.** A limited C#-to-WASI path; spike in v0.2, evaluation for v0.5. Not a general .NET runtime.
5. **MicroVM.** Evaluation for v1.0 for stronger tenant separation.
6. **AOT-promoted tool.** A reviewed generated tool is compiled ahead of time and runs as a trusted, signed tool with a narrower capability set; v0.5 MVP.

The tier is part of the policy and the audit record. A run must state which tier it used. Security claims must name the tier and the assumptions.

## Consequences

Positive:

- Users can choose a boundary that matches their threat model.
- The v0.1 implementation can be credible without claiming multi-tenant hardening.
- The escape corpus and threat model can grow tier by tier.

Negative:

- More concepts for users to learn.
- Some features will be unavailable in weaker tiers.
- Each new tier requires its own threat model, tests, and operational guidance.

## Alternatives considered

- **One container boundary for every use case.** Rejected: it would either overstate safety for hostile multi-tenant use or under-deliver for trusted local use.
- **gVisor first.** Rejected: it adds operational complexity before the basic compile-and-run loop is proven.
- **WASM-only.** Rejected: the C# WASM toolchain cannot yet run the full set of .NET analysis libraries expected by the killer demo.
- **MicroVM first.** Rejected: too heavy for the local developer experience and too early for the v0.1 scope.
