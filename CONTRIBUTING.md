# Contributing to Runesgalder

Runesgalder is pre-alpha. The runtime is changing quickly, and the public contracts are not stable yet.

## Before you start

- Open an issue or discussion before substantial work so we can agree the scope and design.
- All code and documentation contributions require acceptance of the [CLA](CLA.md), version 1.0, effective 2026-11-23. You retain copyright ownership and grant the Project Owner the licence described in the CLA.
- Bug reports, design feedback, and reproducible test cases in issues do not require a CLA unless they are submitted for inclusion in the repository.
- Security reports must follow [SECURITY.md](SECURITY.md). Do not report vulnerabilities in a public issue.

## Development requirements

- .NET SDK 10.0.401 or later in the 10.0 feature band.
- Git.
- Linux is required for the sandbox daemon. Windows and macOS developers can use WSL2, Docker, or a remote Linux host for sandbox work.
- Docker is not required for the current Flow build and tests.

## Build and test

```bash
dotnet build Runesgalder.sln -c Release
dotnet test Runesgalder.sln -c Release
dotnet format Runesgalder.sln --verify-no-changes
```

The build treats warnings as errors. Public API members require XML documentation. The repository uses central package management and deterministic builds.

## What we look for

- Small, focused changes with a clear reason.
- Tests for new behaviour and for bug fixes.
- A security impact note for changes that touch policy, generated code, journal, artefacts, network, files, processes, secrets, or telemetry.
- British English in documentation and user-facing text.
- No secrets, tokens, private keys, customer data, or machine-specific paths.
- No new dependency without a stated need, licence check, and security review.
- No changes that weaken the default-deny posture without an explicit design decision.

## AI-assisted contributions

AI-assisted contributions are allowed. Disclose the tools you used in the pull request. You remain responsible for the correctness, licence provenance, and security of everything you submit. Do not submit generated code that you cannot explain or maintain.

## Commit messages

Use a short imperative subject that explains the reason for the change. Keep unrelated changes in separate commits. Do not include generated noise or secrets in a commit.

Examples:

```text
Add typed step retry events
Reject rooted sandbox paths
Document the Linux-first v0.1 boundary
```

## Pull requests

A pull request should include:

- The problem and the chosen approach.
- The tests that prove the change.
- Any security or compatibility impact.
- Documentation changes, if behaviour is user-visible.
- Confirmation that `dotnet format --verify-no-changes` passes.
- Confirmation that you have accepted the CLA. CLA Assistant will record this automatically once the integration is configured.

Large changes that affect public APIs, security boundaries, release packaging, governance, or the product direction require explicit maintainer acceptance before merge. Small changes can be accepted by the maintainer after CI and local evidence.

## Reviews and acceptance

The project records acceptance decisions in [docs/acceptance/](docs/acceptance/). Small updates are accepted by the maintainer after tests and evidence. Large updates require the project owner's explicit acceptance.

## Licence

By contributing, you agree that your contributions are licensed under the MIT Licence and the [CLA](CLA.md), version 1.0, effective 2026-11-23. You retain copyright ownership of your contributions. All pull requests require acceptance of the CLA; corporate contributors must provide a signed Corporate CLA.

## Code of conduct

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
