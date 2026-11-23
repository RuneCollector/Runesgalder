# Escape corpus

This directory will contain non-destructive test cases that attempt effects the sandbox must deny. The corpus is evidence for the isolation model, not an exploit library.

## Status

The corpus is planned and not yet populated. The sandbox engine and Linux worker are under construction. Do not run these cases against a shared host or a system you do not own.

## Purpose

Each case answers one question: given a policy and an isolation tier, does a prohibited effect fail closed with a stable diagnostic?

The corpus is expected to cover:

- Network access, DNS, metadata services, and egress attempts.
- Filesystem reads, writes, path traversal, symlinks, and device files.
- Process creation, signals, and process discovery.
- Environment variable and secret discovery.
- Reflection, dynamic code, `Assembly.Load`, and serialisation abuse.
- P/Invoke, unsafe code, native libraries, and platform invocation.
- CPU, memory, wall-clock, process, file-descriptor, and output exhaustion.
- Encoding, Unicode, path normalisation, and parser edge cases.
- Host-contract abuse and confused-deputy attempts.
- MCP tool-boundary abuse.
- Supply-chain and artefact-cache attempts.

## Case format

A case will use a directory or a small JSON document with fields such as:

```json
{
  "id": "network-denied-basic",
  "category": "network",
  "description": "Attempt an outbound HTTP request with the default policy.",
  "tier": "linux-container-hardened",
  "policy": "default",
  "source": "NetworkDenied.cs",
  "expected": "policy_denied",
  "notes": "Must fail before any connection is attempted."
}
```

Expected outcomes should be stable codes such as `compile_error`, `policy_denied`, `resource_exceeded`, `timed_out`, or `runtime_error`.

## Safety rules

- Cases must be non-destructive.
- Do not include real credentials, tokens, private keys, personal data, or host paths.
- Do not target third-party services or shared infrastructure.
- Do not include malware, persistence, or lateral-movement behaviour.
- Run cases only in a disposable Linux environment with network disabled and no host mounts.
- A case that needs a network target must use a local, controlled endpoint and must be clearly marked.

## Acceptance

A case passes when the prohibited effect is denied or contained, the diagnostic is stable, and the host is unaffected. A case that succeeds in performing the prohibited effect is a release blocker for the tier it targets.

The corpus will be wired into CI only after the Linux worker and container boundary exist. Until then, the cases are design inputs, not security evidence.
