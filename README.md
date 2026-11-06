# QueryForge

**An open-source, local-first data preparation and analytics workspace built with ASP.NET Core, PostgreSQL and DuckDB.**

> **v0.9 draft (feature branch):** freely movable visual ETL nodes connected in a real **single executable sequence**, deterministic Keep Columns transformation, CSV export, browser-local unsaved workflow drafts and optional server-side OpenAI API or local Ollama planning. See [v0.9 usage and setup](docs/product/reliable-node-studio-v0.9.md). This is a local, single-user developer prototype, not production software.

> **Current status: v0.8 development prototype, draft PR #30, NOT production-ready.**
> The current working application code is on stacked feature branches, **not** on `main`. The project is designed to run privately on your Windows/Mac/Linux workstation through Docker—not as a paid hosted service.

## What you can try in v0.8

| Feature | Actual capability |
| --- | --- |
| Modern unified Studio UI | Source upload, quality overview, prepare, visualize and evidence report in one page |
| Free-position ETL node workspace | Drag nodes anywhere; connect output → input to change executable **single-path order**, no joins or branches |
| Keep Columns | Keep explicit fields using a deterministic, tested transformation |
| CSV export | Download the selected small/derived dataset with spreadsheet-formula escaping |
| Draft restore | Workflow steps and canvas positions persist in local browser storage |
| Hosted AI option | Optional server-side OpenAI API connector, with no raw source rows sent and mandatory manual approval |
| CSV edit and validation | <=2 MiB, <=3,000 rows, <=40 columns; existing deterministic C# quality checks |
| Excel `.xlsx` import | First sheet, first row as column names, converted to bounded CSV; same editable limits |
| Large CSV import | <=25 MiB, <=30,000 rows, <=80 columns; **read-only paged preview**, no v2 ETL/charts yet |
| Easy Mode | Guided, limited offline command patterns create real editable steps |
| Advanced Mode | Drag or click operations; reorder **linear** pipeline; configure settings in Inspector |
| ETL preview / apply | 15 allowlisted operations; real `EtlPipeline`, audit + preview fingerprint, explicit apply into a derived dataset with lineage |
| Charts | Backend-calculated category counts, category sums and numeric histograms with source-backed values |
| **v0.8 Pivot Explorer** | Server-calculated 1D/2D pivots, Count/Sum/weighted Average, exact-decimal grand totals, charts and CSV export; 20×12 category cap |
| Report | Deterministic quality/analytics narrative export (JSON), no invented AI numbers |
| Optional natural-language planner | Self-hosted **Ollama**; proposes allowlisted steps, deterministic C# validation, no auto-apply |
| v0.6 typed DAG + DuckDB | Separate synthetic-only compiler/executor demo. **Not yet attached to uploaded user datasets.** |

The ETL UI stays **linear** in this version. The v0.8 Pivot Explorer is real, but full enterprise pivot/semantic-model functionality, branching DAGs, multi-table joins, unrestricted AI, and large-file editable analytics are **not implemented**.

## Installation — Windows, D: drive

Requirements: [Git](https://git-scm.com/), [Docker Desktop](https://www.docker.com/products/docker-desktop/). The .NET SDK is optional if you only use Docker.

```powershell
New-Item -ItemType Directory -Force D:\Projects | Out-Null
Set-Location D:\Projects
git clone https://github.com/mickeymosa/QueryForge.git
Set-Location D:\Projects\QueryForge
git fetch origin
git switch --track origin/feat/pivot-analytics-studio-v0.8
docker compose up --build -d
docker compose ps
```

Visit **http://127.0.0.1:5080**. The original UI remains at `/legacy.html`; old `/etl.html`, `/analytics.html` and `/ingest.html` routes are preserved.

If the repository is already cloned, use:

```powershell
cd D:\Projects\QueryForge
git fetch origin
git switch --track origin/feat/pivot-analytics-studio-v0.8
docker compose up --build -d
```

Don't use `docker compose down -v` unless you intentionally want to delete your local PostgreSQL data. The Docker Compose ports are bound to `127.0.0.1` only.

### The five-minute walkthrough

1. Open QueryForge, click **Try sample data** to upload a synthetic CSV.
2. Inspect the reported quality issues and source row preview.
3. Open **Prepare & ETL**. In Easy Mode, enter `trim extra spaces and remove duplicate order_id` and choose **Guided commands**.
4. Review the same resulting steps in **Advanced Mode**, drag/reorder nodes, click a node to edit its arguments, then **Preview changes**.
5. Inspect the before/after counts and audit, then **Apply & save new dataset**. The original data remains stored.
6. In **Visualize & report**, build a category chart and read its actual numeric values and evidence-based report.
7. Return to **Data sources**, choose **Try pivot sales data**, then under Visualize & report use **Pivot Explorer**. Set Rows=`city`, Columns=`category`, Calculation=`Sum`, Value=`amount`. Expected grand total: **EUR 605**, with city totals Al Wakrah 100, Doha 230 and Lusail 275.

For XLSX, save a workbook with a first row of unique headers and a few synthetic data rows; upload it the same way as CSV. Do not test with confidential workbook data.

## Offline guided commands and optional local/hosted AI

**Guided commands** supports a small deterministic vocabulary for trim, deduplication, missing rows, sort and uppercase/lowercase. It is not an LLM.

To enable an optional **actual, local language model**, install [Ollama](https://ollama.com/), choose a model that fits your computer, and opt in:

```powershell
ollama pull llama3.2
$env:QUERYFORGE_OLLAMA_MODEL="llama3.2"
docker compose up --build -d
```

The API talks to the Docker host through `host.docker.internal:11434`; Ollama must be reachable from the API container on that address. No remote AI service is configured by default. For a hosted OpenAI API, see the v0.9 guide; it uses a backend-only key and sends only prompts/column metadata, not dataset rows. The prompt and source column names (not dataset rows) are sent to the locally configured model. AI proposals are checked by the C# ETL engine and **never apply automatically**. If Ollama isn't available, use Guided commands instead.

## Architecture and tests

- Backend: **.NET 10 ASP.NET Core** minimal API, deterministic quality, ETL and typed `PivotService` (weighted average and strict unit checks).
- Metadata/legacy storage: **PostgreSQL** via EF Core; v2 streaming row storage via Npgsql.
- Analytical foundation: **DuckDB.NET** plus a typed DAG-to-SQL compiler; execution against synthetic sample only at this stage.
- UI: dependency-free, responsive HTML/CSS/JavaScript served locally, with native drag-and-drop. A full React/TypeScript graph designer is a future milestone, not a present dependency.
- Excel: **ExcelDataReader** MIT-licensed. CSV: CsvHelper.
- Tests: xUnit and Docker-backed GitHub Actions HTTP/database smoke checks, including CSV/XLSX import and ETL. Manual browser validation is still necessary.

```powershell
dotnet test tests/QueryForge.Api.Tests/QueryForge.Api.Tests.csproj
docker compose logs --tail=100
```

## Technical notes and roadmap

- [Pivot Explorer v0.8: calculations, exact precision, examples, limits and testing](docs/product/pivot-explorer-v0.8.md)
- [Studio v0.7: real user flows, API endpoints, constraints and testing](docs/product/local-first-studio-v0.7.md)
- [ADR-0003: typed pipeline compilation and DuckDB execution](docs/architecture/adr-0003-typed-pipeline-duckdb.md)
- [ADR-0002: streamed CSV ingestion and resource limits](docs/engineering/adr-0002-resource-safety-streaming.md)
- [Reproducible typed compiler JSON example](examples/typed-pipeline-demo.json)
- [Draft PR #30: Pivot Explorer](https://github.com/mickeymosa/QueryForge/pull/30) (stacked on [draft PR #29: Local Studio](https://github.com/mickeymosa/QueryForge/pull/29)) — reviewed/merged **only with owner approval**

## Security disclaimer

Development uses a local-only known test database password, no authentication/authorization and no hardened spreadsheet sandbox or OS-level analytical worker limits. Keep ports local, work with synthetic data and never claim this is production-grade or safe for confidential information. A polished interface is not a substitute for data correctness, security and load testing.

**License:** MIT. Contributions and code review are welcome through the repository's normal process.
