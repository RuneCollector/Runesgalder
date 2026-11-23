# ADR-0007: Journal, idempotency, and recovery semantics

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

Flow runs call models, tools, and generated code. Those effects cost money and are not always safe to repeat. A run must leave an audit trail, and an interrupted run must be able to resume without repeating work that already happened.

The v0.1 product target is a single machine: no distributed scheduler, no external event bus. The journal therefore has to be durable on local disk, cheap to embed, and readable without running the runtime.

## Decision

### Journal

- `IJournal` stays a narrow append-only interface: `AppendAsync` and `ReadAsync(RunId)`. Flow and Sandbox share it.
- `SqliteJournal` implements it over SQLite in WAL mode, versioned with `PRAGMA user_version` (currently 1). Schema: `runs`, `events`, and `checkpoints`.
- Events are stored as a stable tag (`run.started`, `step.completed`, `model.call_completed`, …) plus a JSON payload. Tags are part of the schema; unknown tags fail loudly instead of being skipped.
- Only one writer process per file. Appends are serialised through a gate; reads use a separate connection so streaming a journal cannot block a writer.
- Timestamps are stored in round-trip (`O`) format and timestamps inside payloads are `DateTimeOffset`, so comparisons do not depend on the reading machine's time zone.
- Event payloads carry identifiers, durations, counts, and error codes. Prompt and completion text is not stored. Model events record `ModelId` and token counts only.

### Idempotency

- The idempotency unit is a checkpoint keyed by a caller-supplied string, or by a derived key: `{flow name}:{SHA-256 of the input JSON}`.
- The derived key makes the common case safe by default: re-running the same flow with the same input reuses the stored output.
- Input types must be JSON-serialisable for the derived key; otherwise the caller must supply a key. The failure is an `InvalidOperationException` that names the fix.
- Checkpoints are first-write-wins. Replaying an effect never overwrites the record created by the first successful execution, so a nondeterministic replay is visible rather than silently accepted.
- The stored output type is recorded by full name and checked on restore; a key reused across flow shapes fails instead of deserialising into the wrong type.

### Recovery

- `Flow.Checkpoint(store, key)` can be applied at any point in a linear composition. It checkpoints the prefix built so far, so `Agent(...).Then(slowTool).Checkpoint(...).Then(next)` resumes at `slowTool` rather than repeating the model call.
- A checkpoint hit appends `CheckpointRestored` and skips the prefix. A miss appends `CheckpointStored` after the prefix completes.
- A checkpoint is recorded only after the prefix succeeds. A crashed or failed prefix leaves no checkpoint, so retry and recovery follow the same path.
- `IJournal` remains the source of truth. Checkpoints are a lookup index over prior successful work, not a replacement for the event log.

### Events and tracing

- `Flow.BeginRun` returns a `FlowRun` with `RunId`, `Result`, and a live `Events()` stream. Events are delivered in append order through an in-process channel and the stream ends when the run ends, including when the run fails.
- The stream is observational: cancelling it does not cancel the run.
- `FlowDiagnostics.ActivitySource` (name `Runesgalder.Flow`) emits an activity per run and per step, tagged with `run.id`, `flow.name`, `step.id`, and model attributes. No OpenTelemetry SDK is referenced; hosts opt in by registering the source.

## Consequences

Positive:

- Interrupted runs resume without repeating model or tool effects, and the journal shows why.
- The whole audit trail is a single local file that any SQLite tool can inspect.
- Event streaming needs no broker and stays inside the process for v0.1.
- Tracing is dependency-free and works with any OTel exporter the host chooses.

Negative:

- Checkpoint outputs must be JSON-serialisable, which constrains what a checkpointed prefix may return.
- Recovery is at checkpoint granularity. Steps between two checkpoints repeat; users must place checkpoints deliberately.
- One writer per file. Concurrent processes need separate files or a future daemon-owned store.
- A separate `idempotency_keys` and `outbox` table is deferred. The current design keys checkpoints by idempotency key directly; outbox lands with the approval and external-action combinators, where it has a consumer.
- `Microsoft.Data.Sqlite` brings a native SQLite dependency into the Flow package. If that becomes a packaging problem, the journal can move to its own package without changing `IJournal`.

## Alternatives considered

- **Append-only files with one JSON object per line.** Rejected: no indexes, no transactional checkpoint writes, and reimplementing query and crash safety for no gain.
- **Entity Framework Core.** Rejected: a large dependency and migration machinery for three tables.
- **In-memory journal as the primary store.** Kept as a test and local-run implementation, but it cannot support recovery.
- **Overwrite checkpoints on replay.** Rejected: it hides nondeterminism and can silently replace a good record with a bad one.
- **Store assembly-qualified type names for events.** Rejected: assembly-version churn breaks reads. Explicit event tags plus typed payloads keep the file stable.

## Dependency review

- Package: `Microsoft.Data.Sqlite` 10.0.15.
  - Need: durable, transactional local journal and checkpoint store.
  - Licence: MIT (Microsoft). Bundles SQLite via `SQLitePCLRaw` (MIT).
  - Security note: the database is local and schema-owned; no SQL is built from user input beyond parameterised values.
- Package: `Microsoft.Extensions.AI.Abstractions` 10.11.1.
  - Need: the `IChatClient` abstraction that Flow's `Agent` step targets.
  - Licence: MIT (Microsoft).
  - Security note: abstractions only; no provider, no network calls, no credentials. Model adapters are supplied by the host.