# QueryForge v0.8 — Pivot Explorer

**Status:** Draft, stacked feature release. **Pull request:** [#30](https://github.com/mickeymosa/QueryForge/pull/30) into unmerged v0.7. Local-only, non-confidential test data only.

## What the user can do

1. Start QueryForge locally at `http://127.0.0.1:5080`, select **Try pivot sales data**.
2. Open **Visualize & report** and scroll to **Pivot Explorer**.
3. Configure **Rows = city**, **Columns = category**, **Calculation = Sum**, **Value field = amount**.
4. Click **Build pivot table**. QueryForge calls the read-only C# endpoint, not a fake JavaScript calculation.
5. Inspect the multi-dimensional table, row totals, column totals, grand total, quality rules and data fingerprint.
6. Switch between a bar comparison and a line comparison of the **same row totals**. A line connects alphabetically ordered categories, so it does **not** imply a chronological sequence.
7. Export a CSV containing the pivot labels and exact calculated values. The CSV exporter protects against formula injection in untrusted labels.

### Deterministic example

`src/QueryForge.Api/wwwroot/samples/pivot-sales.csv` contains eight synthetic EUR orders:

| City | Hardware | Software | Total |
| --- | ---: | ---: | ---: |
| Al Wakrah | 40 | 60 | 100 |
| Doha | 150 | 80 | 230 |
| Lusail | 125 | 150 | 275 |
| **Grand total** | **315** | **290** | **605** |

Correctness is checked independently in both the HTTP/Docker smoke and real headless browser E2E tests.

## Source-backed contracts and trust boundaries

`PivotService.cs` uses only `DatasetEntity.Rows` from an existing **small editable dataset**. No arbitrary SQL, LLM, Python, C#, file paths or user-controlled function names are executed.

```http
POST /api/datasets/{id}/pivot/preview
Content-Type: application/json
```

```json
{
  "rowDimension": "city",
  "columnDimension": "category",
  "aggregate": "Sum",
  "measure": "amount",
  "excludeFlaggedRows": true
}
```

Response includes `datasetId`, `datasetSha256`, exact selected row/column names, `unit`, `includedRows`, `excludedRows`, `invalidNumericRows`, `columnLabels`, `rows[].values`, `rows[].total`, `columnTotals`, `grandTotal`, and explanatory notes. Every number is computed by the .NET 10 backend.

**Exact numeric values are encoded as invariant-culture decimal strings in the JSON response**, not JSON numbers, to preserve precision across JavaScript's `Number` boundary. The charts use approximate numeric coordinates for screen placement but display the **exact text** returned from the backend. CSV exports retain exact text.

## Data correctness rules

- **Count** counts every eligible row. The quality-exclusion checkbox removes rows flagged by the profiler unless the user explicitly includes them.
- **Sum** sums only values parseable as invariant-culture decimals; **Average** computes `sum / number of valid values`. Row/column/grand averages are calculated from underlying records, not by averaging the already-aggregated cells.
- **Mixed or missing currency:** Sum/Average are rejected when eligible rows contain multiple/unknown currencies. This conservative release does **not** compute cross-currency grand totals; the user must separate/filter currencies before aggregation. Count is allowed.
- Missing dimension values use an explicit `(missing)` bucket. Missing cells in Sum/Average are `null`, shown as an em dash; missing cells in Count are zero.
- Unknown row, column or measure names, same row and column dimension, unsupported aggregate, and excessive cardinality fail with HTTP 400.
- Categories are capped at **20 row groups × 12 column groups**. Do not silently top-N truncate totals or overwrite any saved dataset.
- The output is a **read-only preview**, traceable to a specific dataset SHA-256. It does not persist an analytical model or alter the source.

## Architecture boundaries

This is a learning-led **vertical slice**, not the full Power BI semantic layer. It uses the existing legacy edit/ETL storage path (CSV or first worksheet of XLSX, up to **2 MiB / 3,000 rows / 40 columns**). The separate v2 import pathway (up to 25 MiB) and the synthetic-only DuckDB DAG execution demo remain disconnected.

We deliberately added the pivot engine as a standalone typed `PivotService` plus `PivotEndpoints`; the Studio loads its own JS module `qf-pivot.js` and receives only selected dataset metadata via a browser custom event. The original chart system, ETL, data quality, and optional local Ollama remain unchanged.

## Testing and evidence

- xUnit `PivotServiceTests.cs`: two-dimensional sums, weighted averages, quality inclusion/exclusion, invalid numeric counts, missing dimensions, unsupported operations, unknown fields, cardinality guard, mixed-currency/unknown-currency rejection, empty datasets, source immutability, and exact high-precision decimal transport.
- Docker HTTP checks: real CSV upload → pivot Count by paid/currency → Avg by paid → mixed-currency Sum rejection → unknown dimension rejection.
- Headless Chrome: complete v0.7 upload → ETL → chart flow; v0.8 pivot table/linked line chart; 390px viewport; Excel upload; synthetic sales CSV with **Doha 230, Lusail 275, Al Wakrah 100, total EUR 605**.
- CI screenshots in the run's `queryforge-studio-e2e` artifact, if the run succeeds, retained for 7 days. Manual owner testing on Windows is still recommended.

## Next bottlenecks in priority order

1. **Real semantic layer:** Measures and dimensions with explicit type/unit metadata, filter contracts and dataset-version keys; persist reusable chart/pivot definitions.
2. **Large analytical datasets:** Parquet staging and real user dataset execution via an isolated DuckDB worker with OS resource limits, cancellation and memory/p95 benchmarks.
3. **True visual DAG:** multiple inputs, typed joins, schema propagation, failure and restart semantics, durable pipeline graphs; not just a drag-reordered linear list.
4. **AI intent → visual analytics:** optional on-device model proposes a typed pivot or chart definition with schema and unit validation, then waits for the user's approval.
5. **Local product hardening:** Excel workbook safety, versioned DB migrations, auth for any future shared deployments, keyboard accessibility, Windows UI acceptance, and installer/package UX.

## CV statement (after local review)

> Built a local-first data-preparation and analytics Studio using .NET 10, PostgreSQL and a tested web interface. Implemented preview-and-approve ETL, bounded two-dimensional pivot aggregation with exact-decimal serialization and weighted averages, source-backed charts, CSV/XLSX imports and a safe optional local AI proposal workflow.

**Don't claim** large-scale out-of-core pivots, Parquet processing of user uploads, a complete BI semantic model, Excel-level calculated fields, licensed enterprise pivot parity, or an automatically executing AI agent.
