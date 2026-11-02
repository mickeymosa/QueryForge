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

All analysis is deterministic. There is **no LLM, free-form SQL execution, authentication, tenant isolation, or production database connector** in this milestone.

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
