# QueryForge

**A self-hostable, learning-led data quality and analytics platform.**

> **Development status (November 2026): Local-only prototype. NOT production-ready.**
> The full code is currently on stacked draft branches; `main` contains the initial planning material. This branch is **v0.6**, based on v0.5. Please do not upload confidential datasets or expose its ports to the Internet.

## What exists on this development branch

| Capability | Status |
| --- | --- |
| .NET 10 API, PostgreSQL, Docker Compose | Local prototype |
| CSV quality checks and controlled ETL/visualization | Legacy small-data path (2 MiB, 3,000 rows) |
| Incremental CSV ingest with paged DB row reads | Separate v2 route (25 MiB, 30,000 rows); **large-data ETL is NOT connected** |
| Typed DAG compiler (Source/Filter/Select/Aggregate) | Implemented with Kahn sorting, schema/type checks and SQL parameters |
| DuckDB analytics execution | **Only** the shipped synthetic `demo_orders` file; restricted preview, max 100 result rows |
| Easy/Advanced React workspace, real-world Parquet execution, visual branching DAG | Not implemented |
| AI agent/LLM, user accounts, multi-tenant permission boundary | Not implemented |

**Do not confuse the typed DAG compiler with a full SSIS/Power BI replacement.** The analytical foundation has intentionally narrow scope so it can be reviewed and tested.

## Run locally

Requires Docker Desktop. Run from the cloned repository root:

```powershell
git fetch origin
git switch --track origin/feat/typed-dag-duckdb-foundation-v0.6
docker compose up --build -d
docker compose ps
```

Visit:
- `http://127.0.0.1:5080`: legacy small-data ETL and analytics screens.
- `http://127.0.0.1:5080/ingest.html`: separate streaming import and paged preview.

### Run the new typed pipeline demo

The only available pipeline source is a **synthetic, server-approved sample**. Its data includes EUR and USD records, so aggregations are explicitly grouped by currency.

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:5080/api/pipelines/compile `
  -Method Post -ContentType 'application/json' -InFile examples/typed-pipeline-demo.json

Invoke-RestMethod -Uri http://127.0.0.1:5080/api/pipelines/demo/preview `
  -Method Post -ContentType 'application/json' -InFile examples/typed-pipeline-demo.json
```

Expected synthetic aggregation after the positive-amount filter: `EUR=325`, `USD=50`. Run the unit tests using the .NET 10 SDK:

```powershell
dotnet test tests/QueryForge.Api.Tests/QueryForge.Api.Tests.csproj
```

Stop without deleting your PostgreSQL data:

```powershell
docker compose down
```

**Never run `docker compose down -v` unless you intentionally want to erase local database volumes.**

## Why the architecture is changing

The initial implementation relied on `List<Dictionary<string,string>>` and a serialized JSON dataset value. That approach was simple for early learning but creates memory amplification and poorly scalable analytical access. v0.5 introduced a separate incremental importer and individual PostgreSQL row storage. v0.6 introduces the shared typed DAG compiler and restricted DuckDB execution demo.

We are evaluating **PostgreSQL for metadata + Parquet/DuckDB for analytical data**. This is an architecture direction, **not yet implemented for uploaded user datasets**. Arrow is optional interchange, not a requirement for every module.

- [ADR-0002: Resource safety and streaming storage](docs/engineering/adr-0002-resource-safety-streaming.md)
- [ADR-0003: Typed DAG compiler and DuckDB](docs/architecture/adr-0003-typed-pipeline-duckdb.md)
- [Example pipeline JSON](examples/typed-pipeline-demo.json)
- [Draft PR for v0.6](https://github.com/mickeymosa/QueryForge/pull/28)
- [GitHub Actions evidence](https://github.com/mickeymosa/QueryForge/actions/runs/41695265772)

## Security and release readiness

This branch has **no authentication, tenant isolation, durable job orchestration or verified concurrent production workload**. DuckDB's internal memory setting does not impose a hard operating-system RSS limit; the in-process runner is deliberately restricted to synthetic data. A production worker with OS resource limits, versioned storage, ownership and permissions, migrations and load tests is still required.

AI is not connected to the DAG engine. A later AI planner may propose typed, validated plans; it will not get unrestricted SQL, Python or C# execution.

No pull request is auto-approved or auto-merged. A maintainer must review the code, test results, data semantics and remaining risks before promotion.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and the [engineering playbook](docs/learning/engineering-playbook.md).

**License:** MIT. This is an early open-source learning project, not a production data platform.
