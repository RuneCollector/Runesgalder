# ADR-0003: Linux-first sandbox for v0.1

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

A credible sandbox needs strong process isolation, user namespaces, cgroups v2, seccomp, and a narrow host interface. Linux provides those primitives in a form that can be shipped in a local daemon and in a container image. Windows has strong isolation options, but Win32 job objects, AppContainer, and container support would be a separate implementation with different semantics and a different security story.

The project also needs a cross-platform developer experience. Most .NET developers use Windows or macOS, while production sandbox execution is expected to run on Linux.

## Decision

The v0.1 sandbox runs on Linux. A Linux daemon, `sandboxd`, owns the isolation boundary and the worker lifecycle. The client API is cross-platform.

- Linux: full sandbox support.
- Windows: client, development, and WSL2 or Docker for sandbox execution.
- macOS: client and Docker or a remote Linux host for sandbox execution.
- Windows native strong isolation is deferred beyond v0.1.

The sandbox API must not leak Linux-specific concepts into host code. Platform-specific details belong behind the daemon and its policy model.

## Consequences

Positive:

- One isolation implementation with a coherent threat model for v0.1.
- Linux CI can exercise the real boundary.
- The cross-platform client keeps the .NET developer experience usable on Windows and macOS.

Negative:

- Windows users cannot run the sandbox natively in v0.1.
- Documentation must be explicit about the WSL2/Docker path.
- A future Windows implementation will need its own ADR, threat model, and tests.

## Alternatives considered

- **Windows-first.** Rejected: weaker fit for the required isolation primitives and a smaller overlap with server deployment.
- **A cross-platform daemon in v0.1.** Rejected: it would double the security surface before the first backend is credible.
- **A managed cloud sandbox first.** Rejected: it adds hosting, tenancy, and data-processing questions that are out of scope for v0.1.
