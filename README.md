# QueryForge

**Trustworthy, reproducible data analysis for small teams.**

> **Status: Planning / Sprint 0. No production-ready application exists yet.**

QueryForge is an open-source, self-hostable platform planned to help people import CSV datasets, identify data-quality problems, explore safely, and obtain evidence-backed analyses. Optional AI will propose queries and explanations; deterministic validation, permission checks, and reproducible SQL remain the source of truth.

## Problem

A polished chart can still be wrong when its source data contains duplicates, missing records, inconsistent currencies, or undocumented transformations. QueryForge aims to make data quality and traceability visible before users trust an answer.

## Planned MVP

1. Upload a bounded CSV file to an isolated workspace.
2. Profile schema, missing values, duplicates and invalid records.
3. Review and apply versioned validation rules.
4. Execute constrained, read-only SQL queries with visible provenance.
5. View tables/charts and export a report with source details.
6. Optionally request a natural-language explanation with the supporting SQL displayed.

AI is **optional**. The core must work without an API key or cloud subscription.

## Proposed stack

- **C# / ASP.NET Core (.NET 10):** primary API, domain modelling, authorization and orchestration.
- **PostgreSQL + EF Core / Npgsql:** application metadata, workspaces, structured analysis data.
- **Python:** testable CSV ingestion and data-quality processing, added after .NET groundwork.
- **React + TypeScript:** accessible dataset management and visualization UI.
- **xUnit / pytest / Vitest / Playwright:** tests at different boundaries.
- **Docker Compose + GitHub Actions:** repeatable local setup and CI, implemented in later sprints.

The above is an architectural *proposal*; tool choices can change through documented architecture decisions.

## Current contents

- [Product charter](docs/product/charter.md)
- [MVP requirements](docs/product/mvp-requirements.md)
- [Roadmap and epics](docs/planning/roadmap.md)
- [Sprint 0](docs/planning/sprint-0.md)
- [Architecture decision 0001](docs/architecture/adr-0001-architecture.md)
- [Skill evidence matrix](docs/learning/skill-evidence-matrix.md)
- [Engineering and mentorship agreement](docs/learning/engineering-playbook.md)

## Getting involved

See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md). This is a learning-led early project; please do not use it with real confidential or production data.

## License

MIT; see [LICENSE](LICENSE). The maintainer can review licensing before the first public release.
