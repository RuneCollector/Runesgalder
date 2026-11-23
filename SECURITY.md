# Security policy

## Supported versions

Runesgalder is pre-alpha. No version is currently supported for production use. Security fixes are applied to the `main` branch while the project is pre-release.

## Reporting a vulnerability

Please use GitHub Security Advisories for this repository as the primary channel. Do not open a public issue for a suspected vulnerability.

If you cannot use GitHub Security Advisories, email the project owner at:

**3070687583@qq.com**

This is the only security contact for the project. Do not send security reports to any other address.

Include, where possible:

- A description of the issue and its impact.
- The affected commit, version, or component.
- Reproduction steps or a minimal proof of concept.
- Relevant logs, traces, or configuration, with secrets removed.
- Whether the issue requires a compromised host, a malicious model, a malicious dependency, or only normal use.

## Response targets

These are targets, not guarantees:

- Acknowledge a report within 7 days.
- Complete initial triage within 14 days.
- Agree a disclosure plan after triage and share a fix or mitigation when available.

The project does not currently operate a bug bounty programme.

## Safe harbour

Good-faith security research is welcome when it is limited to:

- Your own local deployment of Runesgalder.
- A repository fork or a disposable test environment that you control.
- Non-destructive tests that do not access, modify, or exfiltrate data you do not own.
- Tests that do not degrade shared services, CI, package registries, or third-party infrastructure.

Do not use this policy as permission to test systems you do not own or to run denial-of-service attacks.

## Scope

In scope:

- The Flow runtime, journal, policy, and event model.
- The sandbox contract, policy engine, compile pipeline, worker isolation, and daemon.
- The MCP server and its tool boundaries.
- Build, release, and supply-chain configuration in this repository.
- Handling of secrets, prompts, generated code, logs, artefacts, and telemetry.

Out of scope for the current phase:

- Findings that require a compromised host kernel, hypervisor, container runtime, or CI platform.
- Social engineering, spam, and physical attacks.
- Reports produced only by an automated scanner without a reproducible issue.
- Denial-of-service against shared infrastructure.
- Vulnerabilities in third-party dependencies that have no Runesgalder-specific impact; report those upstream and tell us if Runesgalder is affected.

## Security model and current limitations

The v0.1 target is local, single-tenant execution with a Linux container boundary. It is not a claim of hardened multi-tenant isolation. The project will publish conservative claims, test evidence, and residual risks as the sandbox is implemented.

Important current limitations:

- The sandbox engine is not implemented yet.
- There is no production-hardened isolation boundary in this repository yet.
- Windows native strong isolation is deferred; use WSL2 or Docker for development.
- Generated code must not receive arbitrary host files, credentials, or network access.
- No absolute security claims are made.

See [the threat model](security/threat-model.md) for the current analysis.

## Telemetry

There is no phone-home behaviour. Local logging and local OpenTelemetry are the intended defaults. Any remote telemetry would require explicit opt-in and a separate review.

## Disclosure

We prefer coordinated disclosure. We will credit reporters who wish to be credited, unless they ask to remain anonymous. Please give the project a reasonable opportunity to fix the issue before publishing details.
