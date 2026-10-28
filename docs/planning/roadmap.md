# Product Tree and Roadmap

## Work hierarchy

**Vision → Release → Epic → User Story → Task → Test/Evidence**

Each task should link to acceptance criteria and a verifiable outcome. Avoid disconnected "technology practice" masquerading as product features; run separate learning exercises when appropriate.

## Releases

| Release | Target outcome | Effort range (cumulative estimate) |
| --- | --- | --- |
| v0.1 Technical demonstration | C# API, tested metadata endpoint, PostgreSQL setup | 60–90h |
| v0.2 MVP | Local CSV import, quality rules, safe read-only analytics, charts | 180–240h |
| v1.0 Open-source release | Reliability, docs, security hardening, optional AI evaluation | 360–450h |
| Learning extension | Java/C++/cloud/networking and deep performance exercises | 550–750h total |

Effort estimates are uncertain; inspect actual velocity after two iterations.

## Epics

- **E01 — Foundations:** project setup, ADRs, C# API, first tests, PostgreSQL and CI.
- **E02 — Ingestion & Provenance:** CSV upload, parsing, dataset version and profiling.
- **E03 — Trust & Validation:** validation rules, missing/duplicate data, repeatable quality reports.
- **E04 — Secure Exploration:** least-privilege SQL queries, resource guards, access tests, audit.
- **E05 — Reporting:** tables, charts, exports and provenance display.
- **E06 — Optional AI:** query proposals, explanation, eval suite, safe fallback.
- **E07 — Release Engineering:** Docker, CI/CD, observability, docs, contributions and threat model.

## Kanban states

Backlog → Ready → In progress → Review → Done

Definition of Ready: user value, acceptance criteria, dependencies and owner are clear.

Definition of Done: implementation, tests, documented decisions, linked PR, and a five-minute explanation from the author.
