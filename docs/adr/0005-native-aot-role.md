# ADR-0005: Native AOT roles in the sandbox

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

Native AOT produces a self-contained executable with no runtime code generation. That is valuable for a small, fast, low-dependency daemon. It also means Native AOT cannot execute ad-hoc C# that is generated at runtime: there is no JIT, no `Assembly.Load`, and reflection is restricted.

A design that says "use Native AOT to run AI-generated C#" is therefore incorrect. The project needs to state what AOT is for and what executes the generated code.

## Decision

Native AOT has four roles:

1. **AOT control plane.** The Linux `sandboxd` daemon, its policy engine, host interface, and lifecycle management.
2. **AOT host harness.** A small, reviewed host that provides the contract assembly and the data bridge for a known worker shape.
3. **AOT promotion of approved generated tools.** A generated tool that has passed review and testing can be compiled ahead of time, signed, and shipped as a trusted tool. This is a v0.5 MVP direction, not the default execution path.
4. **AOT-compatible client tooling.** The CLI and MCP server can be published as AOT binaries where dependencies allow.

Ad-hoc generated C# runs in a CoreCLR worker inside the isolation boundary. The worker is started by the AOT control plane, receives a compiled assembly and a narrow contract, and returns structured results. The control plane never loads generated code into its own process.

WASM C# is a separate, limited path. It is not the general answer for the .NET analysis libraries targeted by v0.1.

## Consequences

Positive:

- The daemon is small, fast to start, and has a smaller dependency surface.
- Generated code is isolated from the control plane by construction.
- The promotion path gives a route from untrusted generation to reviewed, trusted tools.

Negative:

- The system has at least two runtime shapes: the AOT control plane and the CoreCLR worker.
- The compile pipeline must produce assemblies that the worker can load safely.
- AOT promotion requires a review process and signed artefacts.
- Some libraries and reflection patterns will not be AOT-compatible; the host contract must stay narrow.

## Alternatives considered

- **AOT-only execution of generated code.** Rejected: technically impossible for arbitrary ad-hoc C# without runtime code generation.
- **Interpreted C# only.** Rejected: too slow, too limited, and a different compatibility story from the .NET libraries users expect.
- **WASM-only.** Rejected for v0.1: the C# WASI path is not yet a general replacement for the CoreCLR analysis workload.
- **Run generated code in the daemon process.** Rejected: it collapses the control-plane boundary and makes escape impact maximal.
