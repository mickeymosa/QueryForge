# QueryForge Local Data Studio — v0.7 product and engineering notes

**Status:** Draft implementation in PR #29, not approved/merged. **Strategy:** local-first, no paid cloud hosting, open-source MIT. This is a developer prototype, not a production multi-user application.

## Product objective

One desktop-browser workspace where a new user can:
1. Drop a CSV file or an Excel `.xlsx` workbook.
2. See column names, bounded row preview and reported quality issues.
3. Use **Easy Mode** (guided commands, optional local Ollama suggestions) or **Advanced Mode** (drag operations into a linear node list and reorder them).
4. Inspect each operation's arguments.
5. Preview with the existing deterministic C# `EtlPipeline` endpoint and audit.
6. Explicitly approve `/etl/apply`, which creates a new source-linked dataset and does not overwrite the original.
7. Build a chart and download an evidence report calculated by existing deterministic analytics endpoints.

**Do not claim**: full Power BI pivots, arbitrary multi-source DAG joins, true LLM if Ollama isn't configured, Excel workbook-wide import, Parquet analytics for uploaded users, or production-ready multi-user security.

## Functional map: UX → API → business engine

| UI action | API | Real backend behavior |
| --- | --- | --- |
| Drop/edit CSV (<=2 MiB) | `POST /api/studio/import` | Reuses `CsvImportService` validation + quality profiling + PostgreSQL storage |
| Drop XLSX (<=2 MiB) | `POST /api/studio/import` | ExcelDataReader 3.9.0 first worksheet → bounded RFC CSV conversion → same importer |
| Drop CSV >2 MiB, <=25 MiB | `POST /api/v2/datasets` | Separate streaming PostgreSQL row importer. **Read-only preview** in Studio |
| Browse data | `GET /api/datasets/{id}/preview` | Validated first 20 records |
| Advanced drag/drop | Local browser `steps[]` | Click/drag/reorder updates the same request structure, not an animation-only view |
| Offline guided text | Local conservative rules | Supported phrases generate draft `TransformStep` objects; NOT an AI model |
| Optional Ollama prompt | `POST /api/studio/assistant/plan` | Local LLM proposes typed steps, then C# `EtlPipeline.Execute` validates a read-only preview; no save |
| Preview ETL | `POST /api/datasets/{id}/etl/preview` | Real step execution, quality after, audit and fingerprint; original preserved |
| Approve ETL | `POST /api/datasets/{id}/etl/apply` | Re-executes same definition with fingerprint approval; persists derived dataset/lineage |
| Generate chart | `POST /api/datasets/{id}/charts/preview` | C# grouping/histogram/sum; browser only renders SVG and numeric data table |
| Report | `GET /api/datasets/{id}/advisor/report` | Deterministic evidence narrative, downloadable JSON; not LLM-generated |

## Constraints and user warnings

- Editable source: <=2 MiB CSV or XLSX, <=3,000 data rows, <=40 columns, <=12 ETL steps.
- Excel: first worksheet only; first row must contain unique non-empty headers; only `.xlsx`; cells are imported as values and converted to CSV under memory limits. Multi-sheet selection, formula semantics, workbook styling and richer data types are not preserved. A small XLSX ZIP can still contain heavy shared strings, so **malicious file hardening remains incomplete**.
- CSV up to 25 MiB takes a separate read-only path; no ETL/charts on that large dataset at this milestone.
- Advanced workspace is **linear**. Drag/drop reorders/adds steps in a sequential pipeline; it is not yet a multi-input DAG with joins or branching. The v0.6 DAG compiler remains a separate synthetic-only demo.
- Existing chart choices: CountByCategory, SumByCategory, Histogram. Currency mixing is rejected by backend safeguards.
- Local data lives in Docker-managed PostgreSQL volumes, not on cloud services. `docker compose down` preserves; `down -v` destroys local data.
- No users, authorization, tenant isolation or CSRF protection yet. Bind localhost only and use synthetic/non-confidential data.

## Optional real AI (no paid API)

Install **Ollama locally** and pull a model supported by your PC hardware, e.g. `ollama pull llama3.2`. On Windows + Docker Desktop, the app calls `http://host.docker.internal:11434/api/chat`. Opt in before starting Compose:

```powershell
$env:QUERYFORGE_OLLAMA_MODEL="llama3.2"
docker compose up --build -d
```

Without that variable, the Ollama button is disabled and **offline guided commands still work**. Nothing is sent to an external LLM by default. Backend requests send only the user's prompt and column-name metadata; they do not send data rows. The model returns JSON with a maximum of 12 allowed operations. The deterministic C# engine rejects unknown columns, invalid parameters, and unsupported operations. The user still has to review each node, run the normal preview, and press Apply. No AI call receives shell/Python/SQL or database-write access.

Local-model output quality depends on model capability and available RAM; vague commands and complex semantic joins are not reliably resolved. If C# validation fails, the request is rejected and the dataset remains unchanged.

## Architecture

- Reuse existing ASP.NET Core + PostgreSQL and original data-quality/ETL/chart engines for small datasets.
- Keep the newer v0.5 streaming importer and v0.6 typed DAG compiler intact rather than silently wiring unsafe paths together.
- UI is dependency-free HTML/CSS/ES2022 JavaScript served by ASP.NET Core static files. This is a pragmatic zero-build local MVP, not a completed React Flow implementation. It can be migrated to React/TypeScript once interactions and API contracts are validated.
- UI stores an in-memory serializable array of `{ operation, column, from, to }`; Easy/Advanced editing and Ollama proposals use the same array. All persistence and analytics run via the server.
- The new frontend is at `/`. Previous index is preserved at `/legacy.html`, existing `/etl.html`, `/analytics.html`, and `/ingest.html` still work.

## How to test on Windows D: drive

```powershell
cd D:\Projects\QueryForge
git fetch origin
git switch --track origin/feat/local-workspace-v0.7
docker compose up --build -d
docker compose ps
```

Then visit **http://127.0.0.1:5080** (not the old `/etl.html`). Import synthetic orders via **Try sample data**. On the Prepare tab, type `trim extra spaces and remove duplicate order_id`; click **Guided commands**; preview and inspect the audit. Approve a new dataset, then open Visualize. Try switching Easy/Advanced and dragging `Filter amount above` from the operations panel; set the actual threshold in Inspector. Confirm invalid column/threshold requests are rejected by the C# backend.

For Excel, create `test.xlsx` with headings `order_id,amount,currency` and a couple of rows. Check the uploaded file appears as a new editable CSV-backed dataset. Try an empty header or a file beyond the stated limit, and verify rejection.

Run CI:
- `dotnet test tests/QueryForge.Api.Tests/QueryForge.Api.Tests.csproj`
- `docker compose up --build -d`
- GitHub Actions backend + Docker smoke, including XLSX and new Studio API.
- Browser usability and keyboard/drag-drop interactions still require **manual Windows review** or a future Playwright browser test; don't call them automated browser E2E tests.

## Bottlenecks ranked

1. **Correct user dataset execution for large files:** v2 streaming rows and v0.6 DuckDB compiler are disconnected. Need Parquet staging, typed mapping and OS-isolated query worker.
2. **Data semantics and real pivot:** charts exist but dimension/measure relationships, calculated measures, time groupings and server-side pivot need an independent semantic layer.
3. **Visual DAG:** current lane is linear. Need typed port contracts, joins, graph persistence, branching, failure handling, proper compile semantics and programmatic execution.
4. **Local AI reliability:** small models hallucinate fields/operations; schema contracts, grounded prompts, negative tests, resource budgeting and offline fallback are necessary.
5. **Spreadsheet behavior:** Excel formula evaluation, multiple sheets, date types, 100+ MiB workbooks and decompression/CPU limits are not supported yet.
6. **Browser quality:** responsive CSS is implemented, but real cross-browser drag/drop, keyboard alternatives, accessibility and end-to-end visual tests need user validation.
7. **Security/operations:** no auth/RBAC, no migration-based schema changes, in-process AI/analytics still need durable jobs/quotas; don't publish a public endpoint.

## CV claim, once personally tested

> Built a local-first .NET 10 data-preparation and analytics prototype with CSV/XLSX import, real preview-and-approve ETL workflows, a drag-reorder visual editor, backend-calculated charts and an optional self-hosted LLM planner constrained by typed validation.

**Do not say:** production-ready Power BI clone, 500MB ETL, React Flow/DAG visual joins, fully autonomous AI or enterprise multi-tenancy.
