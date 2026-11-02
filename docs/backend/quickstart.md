# QueryForge local backend — Quickstart

**Status:** local-only demonstration. Not production-ready. No user accounts or authorization. Use only synthetic/non-sensitive datasets.

## Requirements

- Docker Desktop with Compose, Windows/macOS/Linux
- Optional: .NET 10 SDK for local C# coding and tests

## Start in one command

From repository root, switch to `feat/backend-mvp-v0.1`, then:

```sh
docker compose up --build
```

Open **http://127.0.0.1:5080/** and click **Try an example**.

The browser can upload comma-separated UTF-8 CSV files up to 1 MiB, 3,000 records, 40 columns.

## Commands

```sh
docker compose ps
docker compose logs -f api
docker compose down
dotnet test tests/QueryForge.Api.Tests/QueryForge.Api.Tests.csproj
```

To reset the demo data (destructive):

```sh
docker compose down -v
```

## API endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/health` | Health and PostgreSQL reachability |
| GET | `/api/datasets` | List 100 newest imports |
| POST | `/api/datasets` | Multipart CSV upload, field name `file` |
| GET | `/api/datasets/{id}` | Metadata + deterministic quality report |
| GET | `/api/datasets/{id}/preview?limit=10` | First 1–50 source rows |
| GET | `/api/datasets/{id}/metrics?column=amount` | Numeric sum and average for approved rows |

All analysis is deterministic. When currency values vary, non-reference currency rows are flagged and excluded from the numeric summary; a future version will use user-approved currency policies. There is **no LLM, free-form SQL execution, authentication, tenant isolation, or production database connector** in this milestone.

## Architecture

- `Domain.cs`: records (ADTs), profiler contract, deterministic rules.
- `CsvImportService.cs`: CSV import, resource guards, hashing, quality report and persistence.
- `Persistence.cs`: EF Core PostgreSQL entity and repository abstraction.
- `Program.cs`: dependency injection, HTTP endpoints and error handling.
- `wwwroot/index.html`: progressive three-step interface, no frontend build process.

Persistence uses serialized rows/report within a dataset record to simplify early learning. It intentionally does **not** support large datasets or analytical indexing. A later migration will move data into normalized, versioned tables.

## Security restrictions

The Docker Compose ports bind to loopback (`127.0.0.1`) and contain demo-only credentials. Do **not** expose those ports to the Internet, upload confidential data, or consider the prototype production-ready.

Before hosting publicly, implement authentication, authorization, CSRF controls for cookie-based sessions, database migrations, secrets management, tenant isolation, structured validation, security testing, retention/deletion policies, and a safe query execution layer.

## Verified CI evidence (2 Nov 2026)

GitHub Actions [backend smoke test](https://github.com/mickeymosa/QueryForge/actions/runs/41493511767) passed: .NET build, 12 unit tests, Docker Compose startup, PostgreSQL health, sample upload, mixed-currency checks and an expected 350.50 sum (two valid EUR rows; five excluded rows). This does not replace browser usability, security, or production load tests.

## v0.2 — Optional ETL before analysis (draft feature)

This branch adds a **Prepare and clean this dataset** link after upload. It opens `/etl.html?dataset={id}` in the same local app.

- **Simple Mode:** Trim surrounding spaces (on), optionally drop rows missing values or remove later duplicate IDs (both off by default).
- **Advanced Mode:** Ordered Trim, Uppercase, Lowercase, Replace, FillMissing, DropMissing, DropDuplicates, RenameColumn. Choose exact columns; renamed columns can be referenced in later steps.
- **Preview changes:** No database write. Displays row/issue counts, per-step effects, sample rows and a preview fingerprint.
- **Approve & save:** Creates a new dataset, keeps original source unchanged, stores pipeline definition, parent dataset ID, checksum and source-record mapping in the quality report.

`POST /api/datasets/{id}/etl/preview` with `{ "pipelineName":"Example", "steps":[{"operation":"Trim"}] }`

`POST /api/datasets/{id}/etl/apply` with same JSON and `previewFingerprint` returned by preview.

**Important:** v0.2 stops automatically trimming CSV values at import time, so ETL transformations are visible and explicit. The stored original data is the parsed CSV text (not necessarily an exact byte-by-byte round-trip of the original file); source file SHA-256 is preserved as metadata. The reported source-row reference is a logical parsed-record position, not necessarily a physical file line number for multiline fields or skipped blank lines.

Details: [ETL engineering and study guide](../etl/etl-guide.md).

**Scope:** This feature remains part of a draft parent backend, without production security, migrations, approved metric semantics or complete browser tests. Do not use confidential data.
