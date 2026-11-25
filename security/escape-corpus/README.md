# Escape corpus

This directory contains non-destructive test cases that attempt effects the sandbox must deny. The corpus is evidence for the isolation model, not an exploit library.

## Status

Populated. `cases.json` is the manifest; each entry points to a C# script under `cases/<id>/Source.cs`. `tests/Runesgalder.Sandbox.Tests/EscapeCorpusTests.cs` compiles and, where the worker image exists, runs every case and checks its expected outcome. The test is Docker-gated: without a Linux daemon and the `runesgalder-worker:dev` image it is skipped, so the corpus is verified on the Linux CI job rather than on a developer host.

The narrower behavioural checks live in `tests/Runesgalder.Sandbox.Tests/EscapeTests.cs`. Do not run these cases against a shared host or a system you do not own.

## Purpose

Each case answers one question: given a policy and an isolation tier, does a prohibited effect fail closed with a stable diagnostic?

The corpus covers:

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

The source-scanning cases (network, filesystem, process, reflection, P/Invoke) prove compile-time refusal. The exhaustion and host-contract cases prove runtime containment. MCP tool-boundary abuse is covered by the MCP test project, not by this corpus.

## Case format

`cases.json` holds a `cases` array. Each entry uses these fields:

```json
{
  "id": "network-denied-basic",
  "category": "network",
  "description": "Attempt an outbound HTTP request with the default policy.",
  "tier": "linux-container-hardened",
  "policy": "default",
  "source": "cases/network-denied-basic/Source.cs",
  "expected": "policy_denied",
  "notes": "System.Net is a banned namespace; the request must never reach the loopback port.",
  "wall_clock_seconds": 2,
  "expected_summary": "0|/app"
}
```

`policy` is informational; `wall_clock_seconds` is optional and overrides the default 30-second wall clock. `expected_summary` is optional and asserts the `summary` field of a successful result. Expected outcomes are stable codes: `compile_error`, `policy_denied`, `resource_exceeded`, `timed_out`, `runtime_error`, or `ok`.

Mapping used by the harness:

| Observed | Code |
| --- | --- |
| Compilation fails with `RGS0001` (banned namespace) | `policy_denied` |
| Compilation fails for any other reason | `compile_error` |
| The hard wall-clock deadline expires | `timed_out` |
| A result is written with `success: true` | `ok` |
| `error` is `output-quota`, or the script reports `OutOfMemoryException` | `resource_exceeded` |
| No result file and a non-zero exit code | `resource_exceeded` |
| No result file and exit code 0 | `runtime_error` |
| Any other script error | `runtime_error` |

## Adding a case

1. Add `cases/<id>/Source.cs`. Keep it non-destructive and free of real paths.
2. Append an entry to `cases.json` with the expected code and, if helpful, a note explaining what the case proves.
3. Run the Docker-gated test (`dotnet test tests/Runesgalder.Sandbox.Tests --filter FullyQualifiedName~EscapeCorpusTests`) on a Linux host with the worker image.
4. If the expected code differs from the observed one, decide whether the sandbox or the expectation is wrong. A case that performs the prohibited effect is a release blocker, not a test to relax.

## Safety rules

- Cases must be non-destructive.
- Do not include real credentials, tokens, private keys, personal data, or host paths.
- Do not target third-party services or shared infrastructure.
- Do not include malware, persistence, or lateral-movement behaviour.
- Run cases only in a disposable Linux environment with network disabled and no host mounts.
- A case that needs a network target must use a local, controlled endpoint and must be clearly marked.

## Acceptance

A case passes when the prohibited effect is denied or contained, the diagnostic is stable, and the host is unaffected. A case that succeeds in performing the prohibited effect is a release blocker for the tier it targets.