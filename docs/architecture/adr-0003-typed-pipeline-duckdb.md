# ADR-0003: Typed pipeline compilation and analytical execution

**Status:** Proposed for review; do not merge. **Branch:** `feat/typed-dag-duckdb-foundation-v0.6` (stacked on v0.5).

## Motivation

The previous implementation allowed ETL expressions to operate over `List<Dictionary<string,string>>`, with every analytical query repeatedly materializing source rows. The v0.5 large-file stream importer stores rows individually, but does NOT yet enable large-file ETL/analytics. The product needs a shared validated execution contract before introducing a complex canvas or AI.

## Decision

Build a dependency-light C# class library `QueryForge.PipelineCore` that:

1. Defines one versioned `PipelineSpec` with nodes/edges; Easy, Advanced and AI proposal interfaces must eventually emit the same type.
2. Applies Kahn's topological sort and rejects cycles, duplicate/unknown nodes and disconnected results.
3. Propagates trusted column schemas through each node; validates filter type, project columns and numeric aggregation.
4. Emits DuckDB SQL only from known node operation templates, server-owned source locations, quoted column names and parameterized predicate values.
5. Returns `CompiledPipeline` with SQL, bound parameters, output schema and a bounded preview limit.
6. Is a **compiler**, not a database, UI, job queue, or AI model.

Supported nodes in the first milestone: **Source → Filter → Select → Aggregate**. No joins, branches, scheduling, arbitrary SQL, script expressions, or custom functions. Invalid configurations fail rather than silently executing a subset of a graph.

## Narrow execution proof

The API `DemoPipelineService` uses `DuckDB.NET.Data.Full` to execute compiled plans against **one server-approved synthetic sample** (`demo_orders`). It accepts no file path, query string, SQL text or user-selected table name. It returns at most 100 result rows, with an in-process single-execution gate, one DuckDB thread and a 128 MiB buffer setting.

**Critical security caveat:** DuckDB's memory limit is NOT a process-level memory ceiling, and in-process execution does not offer tenant isolation. Only the shipped synthetic sample is executable in this milestone. User-uploaded datasets, arbitrary Parquet paths and AI-proposed plans cannot execute here.

## ETL/ELT storage choice

- Keep PostgreSQL for users/workspaces/dataset metadata/jobs/permissions (after formal migrations).
- Evaluate Parquet files + DuckDB for analytical values; Arrow can be a high-performance interchange format but **is not mandatory for all pipeline components**.
- Do not build a new custom query engine. Prefer DuckDB plans and pushdown where supported.
- DuckDB CTEs are a convenient logical representation, but they **do not guarantee zero intermediate materialization or bounded memory**.
- Persistent datasets and reusable pipeline results will require versioned files, manifest transactions, data permissions, cleanup and durable job history.

## API examples

POST `/api/pipelines/compile` — compiler-only, returns type/ordering metadata, no SQL path leakage.

POST `/api/pipelines/demo/preview` — executes the same validated pipeline against synthetic `demo_orders`, returns typed columns and at most 100 rows.

Both expect:
```json
{
  "schemaVersion": 1,
  "nodes": [
    {"id":"source","kind":"Source","source":{"datasetKey":"demo_orders"}},
    {"id":"positive","kind":"Filter","filter":{"column":"amount","comparison":"GreaterThan","value":"0"}},
    {"id":"totals","kind":"Aggregate","aggregate":{"groupBy":"currency","function":"Sum","measure":"amount","outputColumn":"total"}}
  ],
  "edges":[{"from":"source","to":"positive"},{"from":"positive","to":"totals"}],
  "outputNodeId":"totals"
}
```

The expected sample grouping includes both EUR and GBP values because **the demo does not convert currencies**. Any business dashboard displaying money must group or filter by currency and enforce a confirmed metric definition. These are technical demonstration results only.

## Why not all proposed solutions immediately?

The supplied architecture recommendations are directionally correct, but several are not universally true:
- **DuckDB does not guarantee OOM-free queries**: blocking operations, memory outside the buffer manager and concurrent workloads still need isolation/limits/benchmarks.
- **One enormous CTE is not always optimal**: some graphs need staging/materialization or multiple SQL operations for fault isolation and semantic correctness.
- **Parameterized SQL does not parameterize table/column identifiers**: schema-validating and quoting identifiers is necessary.
- **An LLM's JSON isn't safe merely because it's syntactically valid**: validate data ownership, sources, permissions, schema, allowed operations and proposed impact before permitting execution.
- **Server-driven pivoting** is a good option, but data engine/runtime security and output cardinality must still be bounded.

## Evidence required before accepting the PR

- Unit tests for cycle detection, unknown and disconnected nodes, field propagation, type mismatches, invalid identifiers, injection-like values, and SQL parameterization.
- End-to-end Docker check that a typed pipeline is compiled and executed against the synthetic source, with deterministic aggregation result.
- Invalid pipeline returns HTTP 400; unsupported source key is blocked.
- No SQL, file path, arbitrary code or database credential is accepted through the request.
- Reproducible environment via .NET 10 / Docker, run CI on the final commit.
- Explicit owner walkthrough; remain draft and unmerged.

## Subsequent bottlenecks, in order

1. **Schema & datasets:** catalog, dataset version and typed column metadata, with EF migrations and permissions.
2. **Out-of-process worker + analytical store:** staged Parquet + DuckDB, bounded resources and cancellation; test 100–500 MiB on specific hardware.
3. **DAG features:** joins, typed derivations, conditional routing, aggregations, error output and stable plan fingerprints.
4. **UX:** React + TypeScript + React Flow editor and a task-based Easy Mode, both editing the same `PipelineSpec`.
5. **Pivot/semantic model:** validated dimensions, measures, filter semantics, server-driven pivot and paged grid.
6. **AI:** structured pipeline proposal, grounded critique and read-only preview; never unrestricted SQL, Python or C#.
7. **Operations:** durable jobs, workspace RBAC, secrets, audit, observability and release tests.

## Reference documentation

- DuckDB.NET ADO.NET usage: https://duckdb.net/docs/basic-usage
- DuckDB memory/workload limits: https://duckdb.org/docs/current/guides/performance/how_to_tune_workloads
- React Flow: https://reactflow.dev/api-reference/react-flow
