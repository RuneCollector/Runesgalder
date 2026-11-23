# ADR-0010: Data-analysis script contract and table API

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

Generated analysis code must read a host-supplied dataset without file, network, process, or reflection access. The contract is already fixed by ADR-0006: a script implements `IAnalysis.RunAsync(ITable table, IScriptContext context)` and the compiler rejects anything else.

The plan requires a deliberately small data API for v0.1: columns, rows, filtering, grouping, aggregation, sorting, and taking a prefix. Without it every script re-implements culture parsing and ordering rules by hand, and the demo already shows that shape of code. The API must be deterministic, teachable in one screen, and safe to expose to untrusted code.

## Decision

- `ITable` stays the raw host view: `Name`, `Columns`, and string-formatted `Rows`. The worker implements it; generated code cannot construct a host table.
- `TableSlice` is an immutable slice with the v0.1 operations:
  - `Filter(Func<IReadOnlyList<string?>, bool>)`
  - `SortByText(column, descending)` with ordinal comparison
  - `SortByNumber(column, descending)` with invariant-culture `decimal` parsing
  - `Take(count)`
  - `GroupBy(column)` returning `TableGroup(Key, Table)` slices
  - `Sum`, `Average`, `Minimum`, `Maximum` over a numeric column
- `TableExtensions.ToSlice()` adapts an `ITable`. The slice lives in `Runesgalder.Sandbox.Abstractions`, so scripts need no extra reference and no host change.
- Ordering is part of the contract: filtering and taking preserve row order; both sorts are stable; groups appear in first-appearance key order and keep their rows in input order. `null` and empty keys form one group keyed by the empty string.
- Numbers use `NumberStyles.Number` with the invariant culture. A non-numeric value fails with `FormatException` naming the row index, column, and value. `Sum` over an empty slice is zero; `Average`, `Minimum`, and `Maximum` throw `InvalidOperationException`.
- Unknown columns fail with `ArgumentException`. The constructor rejects rows whose width differs from the column count.
- Charts stay structured `ChartSpec` values rendered by the host. Scripts never emit SVG or HTML.

## Consequences

Positive:

- Analysis scripts read declaratively and stay short; the demo's repaired script drops manual index and culture handling.
- Results are reproducible because ordering, grouping, and parsing rules are fixed and tested.
- The API adds no dependency and no new isolation surface: it only reads the rows the worker already loaded.
- Failures point at the offending row and column, which makes model-driven repair practical.

Negative:

- Operations materialise lists; a slice is not lazy. Acceptable for the v0.1 row limits, not for large data.
- Aggregation is decimal-only. Dates, text statistics, and multiple-key grouping are out of scope for v0.1.
- Callers must choose `SortByText` or `SortByNumber`; there is no automatic type detection.
- The public surface grows. New operations need the same tests and ADR treatment, so the API is expected to stay small.

## Alternatives considered

- **Raw rows plus LINQ only.** Rejected: each script re-derives index lookup, culture parsing, and ordering, and the demo needed 20 lines where the slice needs 6.
- **A full dataframe engine** (joins, pivots, window functions). Rejected for v0.1: too much surface to test and to keep deterministic before the isolation story is proven.
- **Typed columns with schema inference.** Deferred: inference rules need their own ADR, and CSV input has no types.
- **Reuse `System.Data.DataTable`.** Rejected: mutable, heavier, and invites APIs the sandbox should not expose.

## Limits

- The slice is available only to generated code; host-side code may keep using `ITable` directly.
- Compilation still happens on the host (ADR-0006); the slice operations run inside the worker beside the script.