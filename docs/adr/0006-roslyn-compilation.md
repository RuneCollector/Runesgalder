# ADR-0006: Typed code step contract and compile pipeline

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

The sandbox must turn AI-generated C# analysis code into an assembly before any isolation boundary can run it. The compiler is therefore the first step of the untrusted-code path, and its choice affects startup time, diagnostics quality, dependency surface, and the security story.

The compiler also runs on the host side, before the container boundary exists. Anything that happens during compilation is not isolated.

## Decision

Generated analysis code enters the pipeline through one typed contract and one compiler.

Contract:

- The script must contain exactly one public, non-abstract implementation of `Runesgalder.Sandbox.IAnalysis` with a public parameterless constructor. Zero, several, or non-public implementations are rejected with `RGS0002`, `RGS0003`, and `RGS0004`.
- The contract type name is resolved by metadata name, not by string search, so aliases and unrelated types cannot satisfy it.

Compiler:

- Use `Microsoft.CodeAnalysis.CSharp` (Roslyn) in-process to compile analysis scripts.
- Compile to `OutputKind.DynamicallyLinkedLibrary` with `OptimizationLevel.Release`, deterministic output, embedded debug information, and `allowUnsafe: false`.
- Whitespace-only compiler diagnostics are dropped; warnings and errors are surfaced as structured `ScriptDiagnostic` records with 1-based source positions.
- References come from a fixed whitelist resolved from `TRUSTED_PLATFORM_ASSEMBLIES` plus `Runesgalder.Sandbox.Abstractions`, not from the caller's compilation.
- Source is capped at `MaxSourceBytes` (256 KiB by default); the source hash and assembly hash are SHA-256, lowercase hex, so runs can be identified and compared.
- Banned namespaces (`System.IO`, `System.Net`, `System.Reflection`, `System.Diagnostics`, `System.Runtime.InteropServices`, `System.Runtime.Loader`, `System.Management`, `Microsoft.Win32`) produce `RGS0001` and block emission.

Compilation is **not** a security boundary. Banned-namespace analysis is defence-in-depth and a diagnostic aid. In .NET 10, `System.IO.File` and similar entry points live in `System.Private.CoreLib`, so a reference whitelist cannot remove them. Enforcement must come from the runtime isolation tier in ADR-0004.

## Consequences

Positive:

- No external compiler service to operate, and no round trip for a compile request.
- Rich structured diagnostics that can be shown to the model that produced the code.
- Reference whitelist and contract validation are cheap and testable.

Negative:

- Roslyn is a sizeable dependency and increases the host process footprint.
- Compilation of hostile source is a denial-of-service surface (parse time, memory) and must be bounded by source-size limits and, later, time and memory limits on the compile worker.
- The banned-namespace list will need maintenance and may give a false sense of safety if read as a sandbox.

## Alternatives considered

- **Out-of-process compiler service.** Deferred: adds a service to operate before the basic loop is proven; the same isolation argument already requires a worker for execution, so compilation can move there later if needed.
- **Ahead-of-time compilation only.** Rejected: the product's use case is code generated at run time.
- **No contract validation, fail at run time.** Rejected: it turns a compile-time user error into a run-time failure inside the sandbox, which is slower to diagnose and harder to attribute.

## Dependency review

- Package: `Microsoft.CodeAnalysis.CSharp` 5.12.0.
- Need: the compile pipeline described above.
- Licence: MIT (Microsoft).
- Security note: runs on the host, so it must be treated as trusted code operating on untrusted input. Source size is capped; the compile step is scheduled to move behind the same process boundary as execution.