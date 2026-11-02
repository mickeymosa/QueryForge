# ETL before analytics — v0.2 engineering guide

**Branch:** `feat/etl-configurable-v0.2`  
**PR:** [#19](https://github.com/mickeymosa/QueryForge/pull/19) (draft, unapproved)  
**Prerequisite:** the backend in draft PR #12. **Local demonstration only. Never upload confidential data or expose to the Internet.**

## What existed previously?

**ETL was not complete.** The v0.1 flow was:

`CSV upload -> validate file structure -> parse rows -> profile quality -> load rows as JSON in PostgreSQL -> calculate deterministic metrics`

There was minimal extraction (CSV reading) and loading into storage, but **no independently configurable transformation phase**. Ingestion trimming is a parsing option, not a documented, user-approved ETL workflow. Data quality warnings are not synonymous with cleaning or correcting data.

## What's new?

```text
CSV source (immutable)
  -> [Basic / Advanced transformation configuration]
  -> preview (no writes; reprofile data)
  -> user applies previewed recipe
  -> derived dataset (new ID and SHA-256)
  -> post-transformation quality report + row-level source mapping
  -> existing metric API
```

The original dataset is **never overwritten** by the transformation engine. The preview and stored version contain original-row references, step-level affected row counts, and definition/source checksums. This is an educational versioning mechanism; it is **not** a finished migrations-based dataset versioning system.

## Basic mode

A user may select, in order:

1. **Trim surrounding spaces** in all cells (checked by default).
2. **Remove rows with missing values** for one explicit column (unchecked by default; can lose data).
3. **Remove later duplicate IDs** by one explicit identifier column (unchecked by default; can lose data).

Click **Preview changes** to show counts and a sample. Only **Approve & save new dataset** writes a derived record. No automatic row deletion is enabled.

## Advanced mode — v0.2 allowlist

| Operation | Scope | Semantics |
| --- | --- | --- |
| `Trim` | All or selected column | Removes leading/trailing whitespace |
| `Uppercase` | Selected column | Uses invariant uppercase |
| `Lowercase` | Selected column | Uses invariant lowercase |
| `Replace` | Selected column | Exact, case-sensitive whole-cell replacement |
| `FillMissing` | Selected column | Replaces empty/whitespace-only cells |
| `DropMissing` | Selected column | Deletes rows with empty/whitespace-only cells |
| `DropDuplicates` | Selected column | Retains first nonempty key, removes later duplicates (case-insensitive, trimmed key) |
| `RenameColumn` | Selected column | Renames header if no case-insensitive collision |

Rules execute top-to-bottom, max 12 steps. They are **not arbitrary user code**. No complex expression parser, arbitrary SQL, runtime plugin, or custom Python execution is accepted.

## REST contract

### Preview
`POST /api/datasets/{id}/etl/preview`

```json
{
  "pipelineName": "Normalize and deduplicate",
  "steps": [
    {"operation": "Trim"},
    {"operation": "Uppercase", "column": "currency"},
    {"operation": "DropDuplicates", "column": "order_id"}
  ]
}
```

Preview returns row counts, before/after quality reports, per-step audit, output sample with original CSV row numbers, definition/data hashes, and a `previewFingerprint`. It does **not** save a derived dataset.

### Apply
`POST /api/datasets/{id}/etl/apply` with the exact same JSON plus:

```json
{"previewFingerprint": "<value returned by preview>"}
```

A missing or stale fingerprint returns HTTP 409. This detects preview/configuration mismatch, **not an authorization or approval security control**; the service is unauthenticated and must remain local-only.

A successful apply creates a new dataset entry whose `quality.lineage` contains source dataset ID, source SHA-256, pipeline name/steps, schema version, counts, step audit, and original CSV row numbers. The hash for the derived dataset covers the canonicalized transformed headers and rows. The source import keeps its SHA-256 of the original file bytes, which is **not** directly comparable to the derived canonical-JSON hash unless the hashing convention is shown.

### Existing API still supports
- `GET /api/datasets/{derivedId}`: includes report and lineage
- `GET /api/datasets/{derivedId}/preview`: returns derived rows
- `GET /api/datasets/{derivedId}/metrics?column=amount`: calculates deterministic summaries after the new quality check

## Source map

- `EtlPipeline.cs` — typed operation enum, request/audit/lineage contracts, pure transformation engine, checks, row-level provenance and hashes
- `EtlEndpoints.cs` — REST adapters for preview/apply and persistence of the derived dataset
- `Domain.cs` — optional `QualityReport.Lineage`
- `Program.cs` — ETL DI registration and route mapping
- `wwwroot/etl.html` — simple/advanced ETL builder, preview, approval, saved-version exploration
- `wwwroot/index.html` — link from existing upload/quality flow to optional data preparation
- `tests/QueryForge.Api.Tests/EtlPipelineTests.cs` — transformations, determinism, missing-field errors, original source unchanged, row lineage
- `.github/workflows/backend-ci.yml` — tests plus Docker smoke test with POST preview/apply and reloaded lineage

## Why this architecture?

**Separation of concerns:** `EtlPipeline` does not know about the network or database. That makes it trivial to unit-test many inputs and swap the storage layer later.

**Abstract data operations:** `TransformStep` and `EtlExecution` describe behaviors and outcomes, not UI clicks or database tables. The typed allowlist constrains behavior in a way a free-text script field would not.

**Immutable source:** Transformations operate on copied rows and save results under a new dataset ID. This enables a user to trace derived data to a source and replay transformations.

**Preview first:** Users can see destructive operations' effects and reason about them. A preview fingerprint detects changes between preview and apply, but cannot replace authentication, authorization, CSRF protection, or audit user identity.

**Deterministic operations:** Re-running the same recipe over identical rows yields the same derived data hash and rule hash (timestamps/IDs for saved runs may differ).

## Important limits and open engineering debts

1. **No authentication / tenant isolation / rate limiting** — block public use.
2. **No proper EF Core migrations or normalized dataset version table** — lineage is temporarily serialized into `ReportJson`.
3. **Pipeline persistence** is per derived dataset; there is no independent template/version library or scheduler.
4. **Batch in-memory transformation** capped to 3,000 rows, not a streaming/distributed ETL product.
5. **Quality heuristics** still assume known column names and first nonempty currency as provisional baseline; these are NOT user-approved business rules.
6. **No reverse operation/undo on derived version itself**; the original source remains.
7. **No joins, merge, custom expressions, type inference, schema mapping wizard, enrichment or external data sources**.
8. **No real browser usability test yet**, even when backend/HTTP/Docker checks pass.
9. **Metric sum semantics remain simplistic**, and inconsistent currencies must never be blended.
10. **No security claim** is implied by allowlisting or request checks.

## Study and exercises

1. Draw `CSV -> Extract -> Transform -> Preview -> Save -> Validate -> Analyze`, and explain which phases in v0.1 were not a full ETL pipeline.
2. Explain every field of `TransformStep` and why `enum` is safer than user-supplied code.
3. Trace two example rows through `Trim`, `DropMissing`, `DropDuplicates`; write their original CSV row numbers after every step.
4. Explain why the engine deep copies the rows before it transforms them.
5. Show why the hash of source file bytes differs from the hash of transformed canonical JSON.
6. Build a ninth operation `RemovePrefix` with tests **on your own branch**, and explain how you guard an empty prefix or unexpected encoding.
7. Add a test that a changed recipe cannot be saved with a stale preview fingerprint.
8. Identify remaining P0 security gaps before publishing online.

**Merge gate:** Show code, technical rationale, negative test evidence, local walkthrough and learner signoff. No automatic approval or merge.
