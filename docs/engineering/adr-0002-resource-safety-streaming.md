# ADR-0002 — Memory-safe ingestion and staged relational storage

**Date:** 2026-11-03  
**Status:** PROPOSED — draft PR #27, owner sign-off required  
**Risk severity:** Release-blocking for any public or multi-user installation

## 1. Problem statement and evidence

The original v0.4 implementation loaded a CSV of up to **25 MiB** with:
- `MemoryStream` → `ToArray` (duplicate raw bytes)
- `CsvReader` → `List<Dictionary<string,string>>` (many allocations)
- full `RowsJson` and `ReportJson` (another complete dataset representation)
- `DatasetEntity.Rows` repeatedly deserialized in ETL, charts and metrics
- persistence of the whole dataset as text in a single `datasets.rows_json` column.

**Risk:** A 25 MiB raw CSV can expand many times in memory. Five concurrent requests may allocate much more than five times the file size, and GC pauses/OOMs are plausible. No production load measurement existed; the 5–10x expansion estimate is a **risk hypothesis**, not a measured result.

The v0.4 AI-related code is a deterministic `ReadOnlyEtlAdvisor`, not an LLM agent. The risk of future untrusted LLM proposals and ungrounded claims still blocks autonomous writes.

## 2. Decision: staged cut-over, not a risky rewrite in one commit

Create a separate `/api/v2/datasets` ingestion route with a **bounded streaming pipeline**:

```text
CSV stream (OpenReadStream)
  -> SHA-256 via streamed hash pass
  -> CsvHelper ReadAsync (no whole-file MemoryStream)
  -> validate 1 row and bounded field lengths
  -> parameterized Npgsql INSERT batch (200 rows)
  -> transactional PostgreSQL row storage
  -> compact metadata / counters
  -> paged preview (1–100 rows / request)
```

Existing API `/api/datasets` remains for small CSVs (2 MiB, 3,000 rows, 40 columns), explicitly labeled *legacy*. Advanced ETL/chart/report engines **do not read large v2 datasets yet**. That limitation prevents an accidental route back to the dangerous whole-dataset materialization.

A simple `/ingest.html` page makes the boundary visible to users; it shows row counts and a paged read-only table.

## 3. PostgreSQL representation

`stream_datasets`
- `id` UUID primary key
- `file_name`, `sha256`, `uploaded_utc`, `row_count`
- `headers_json` and compact `profile_json` JSONB

`stream_dataset_rows`
- `dataset_id` UUID foreign key → `stream_datasets` with ON DELETE CASCADE
- `row_number` integer
- `values_json` JSONB for **one row**, not all rows
- Composite primary key (`dataset_id`, `row_number`) supports ordered paging.

**Caveat:** Per-record JSONB eliminates the single giant JSON value and permits paging, but is **not yet a fully typed/normalized analytical warehouse**. Dynamic fields cannot automatically benefit from typed indexes, column statistics or compression. A later schema mapping/type system and targeted indexes/columnar storage are needed.

## 4. Resource budget and admission controls

| Metric / guard | v0.4 | v0.5 target |
| --- | --- | --- |
| Legacy in-memory CSV max | 25 MiB | **2 MiB** |
| Legacy in-memory row cap | 30,000 | **3,000** |
| Stream import file cap | None | **25 MiB** |
| Stream import rows/columns | None | **30,000 / 80** |
| Batch rows held before DB insert | all rows | **200** |
| Streaming cell limit | None | **16,384 characters** |
| Streaming logical-record total | None | **65,536 characters** |
| Global concurrent requests / process | Unbounded | **4 active, zero queued** (HTTP 429 when saturated) |
| Concurrent streaming imports / process | Unbounded | **1 active** (HTTP 429) |
| Paged row response | whole dataset stored in one entity | **1–100 rows** |

The v2 importer retains a bounded **HashSet of fixed-width SHA-256 identifier hashes** for duplicate counts; memory is O(unique keys) but bounded by 30,000 records. SHA collisions are theoretically possible but practically negligible. Import is transactional: failed validation rolls back metadata and rows.

The API-process semaphore and global limiter are **not cluster-wide**. Production multi-instance deployments will need a durable queue, per-tenant quotas, timeout controls and an admission strategy enforced across instances.

## 5. What this does NOT solve

- Streamed ETL, charts, semantic-model processing, scheduled jobs and AI at v2 scale: not yet implemented.
- Public auth, user ownership, API CSRF strategy, secrets, row-level authorization.
- EF Core versioned migrations: schema currently uses idempotent compatibility DDL at startup; **not sufficient for production**.
- Background jobs, idempotency keys, retry-safe jobs, cancellation checkpointing.
- Rich typed SQL/columnar analytics. Dataset metadata and rows remain JSONB-typed at field level.
- Measured peak RSS/GC pauses under five simultaneous users. This requires a representative benchmark with telemetry and results.
- Immutable dataset version graph, deletion/retention user flow and quotas per identity.

## 6. Verification plan and measurable evidence

CI must prove:
1. Build and all unit tests pass on the exact commit.
2. Import a **>10 MiB, 13,000-row** synthetic CSV through v2; verify actual count in `stream_dataset_rows` via PostgreSQL SQL and verify last page has 25 rows.
3. The same >10 MiB upload through legacy API returns 400, not uncontrolled memory allocation.
4. Upload >25 MiB through v2 returns 4xx.
5. Upload 30,001 data rows through v2 returns 400 and transaction rollback means zero partial v2 records remain.
6. Read-only `/api/v2/datasets/{id}/rows` returns at most 100 rows and `hasMore` is correct.
7. Core AI/advisor tests must continue asserting `UsesExternalAi=false`, `HumanApprovalRequired=true`, and no source mutation.

**Not yet measured:** peak process managed heap and RSS, allocation rate, p95 latency, DB write amplification, and GC pause times. Do not claim 5-user readiness until these are tested with a reproducible load rig. Record machine CPU/RAM, input characteristics, actual throughput and error rate.

## 7. Further decisions before release

1. Replace compatibility DDL + `EnsureCreated` with migration-managed schema, and test upgrades on existing user data.
2. Move ETL to bounded row iterators/typed plan nodes; no per-request full-dataset materialization.
3. Move charts to SQL group-by and typed aggregate query plans with explicit units/currency semantics.
4. Add a durable background job queue, leases, retries, progress, cancellation and idempotency.
5. Add authentication, per-dataset authorization, secret storage, audit and rate limits per tenant.
6. Add 5/20/100-client load profile measuring peak RSS, p95, failure rate and GC.
7. Gate optional AI behind a strict read-only tool boundary, structured outputs, evidence checks and user approval for changes.

## 8. Local reproduction

```powershell
git fetch origin
git switch --track origin/fix/resource-safety-and-persistence-v0.5
docker compose up --build -d
```

- `http://127.0.0.1:5080/` — legacy small-data ETL/Analytics Studio.
- `http://127.0.0.1:5080/ingest.html` — separate 25 MiB streamed ingest and paged PostgreSQL preview.
- `GET /api/health` — database check.
- `POST /api/v2/datasets` — streamed upload (multipart `file`).
- `GET /api/v2/datasets/{id}/rows?offset=0&limit=25` — paged rows.

**Important:** Preserve `docker compose down` volume; do not run `docker compose down -v` on data you want to keep. Never expose this local-only build to a public network.
