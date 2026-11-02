# QueryForge product roadmap — approachable SSIS-style ETL, then SSAS-style analysis

**Decision:** Make the data preparation experience much easier than traditional enterprise ETL products, without claiming feature parity or security readiness prematurely.

**Terminology:** Microsoft's **SSIS** is the ETL/ELT and integration platform (sources, transformations, destinations, control/data flows). **SSAS** is the analytic/semantic-model platform (tabular models, measures, relationships and OLAP). QueryForge targets these as **two separate layers**.

References:
- https://learn.microsoft.com/en-us/sql/integration-services/data-flow/transformations/transform-data-with-transformations
- https://learn.microsoft.com/analysis-services/tabular-models/tabular-models-ssas

## Release v0.3 — practical improvements

| User request | Implementation | Important limitation |
| --- | --- | --- |
| 10 MiB+ uploads | 25 MiB server-side max, 30,000 data rows, 80 columns, multipart and Kestrel limits adjusted | Entire file and rows are still kept in memory; limits are protective, not a scalability solution |
| Modern interface | Shared responsive CSS, polished cards, better states, keyboard/reduced-motion support, drag-drop file selection | Browser accessibility audit and representative user testing remain outstanding |
| More ETL | 15 allowlisted operations, including filtering, derived constant/copied columns, substring replace, sort and step ordering | Not a complete SSIS-like DAG, no joins, aggregates, type casting or multiple input sources |

## Stage A — real enterprise-grade ingestion (next)

Replace storing the entire dataset JSON in one PostgreSQL row.
- Stream CSV parsing in bounded batches; upload to staging.
- Explicit quotas by user/team; adjustable per deployment.
- Background job queue with cancellation, progress, retries, idempotency and checkpoints.
- Persist normalized dataset/versions and pipeline definition/run/step audit tables with EF Core migrations.
- Show physical source lines (where available), bad rows, and schema drift.
- Target 100 MiB and beyond **only after** representative load, memory, restart and recovery testing.

## Stage B — visual SSIS-like canvas

- Node-based Source → Transform → Validate → Destination flow with optional branches.
- Typed operations: convert date/decimal/text, derive arithmetic columns, filtering, grouping, aggregation, pivot/unpivot, join, lookup, null-handling, conditional split.
- Connectors: CSV, Excel and PostgreSQL first; cloud sources behind approved credentials.
- Preview after each step; row-level diffs and explicit rejected records.
- Versioned reusable pipelines, scheduled runs, notifications and execution logs.
- Safe expression builder with allowlisted functions, not raw eval, unchecked SQL, or arbitrary Python.
- Output targets with idempotent writes and conflict policies.

## Stage C — SSAS-style semantic analytics

- Establish a typed semantic model: facts/dimensions, relationships, metrics, currencies and business definitions.
- Measures (sum, count, distinct count, averages) with filtering, grouping, date hierarchies and provenance.
- Query planner, performance testing, caching and incremental processing.
- A chart/report editor that never hides the input version, filter semantics or metric unit.
- Role-based object and row access; formal testing before public hosting.

## UX principles

- **Beginner path:** upload → understand problems → recommended *previewable* fixes → trustworthy report, no scripts.
- **Expert path:** drag-and-drop visual nodes, rich editor, advanced type and destination settings, per-node explainability.
- **Transparency:** always show whether a step changes, drops or creates rows, whether the source changes, and why a metric differs.
- **Security:** assume malicious CSV, untrusted connectors and model-generated statements. Public deployments require authentication, authorization, secrets, quotas, threat model and audit.

## Release gate for v0.3

- Build/test and 10+ MiB end-to-end smoke check on the exact commit.
- Validate manual browser flow on Windows/Docker: upload, ETL advanced step reordering, preview, save, analyze.
- Confirm accepted limits in UI and API; test rejection over 25 MiB and over row cap.
- Record known non-production gaps and secure signoff from maintainer.
- No automatic merge of PR #21, #19, or #12.
