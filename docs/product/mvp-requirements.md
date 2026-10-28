# MVP Requirements — Draft

## Functional user stories

**US-01: CSV upload** — As an analyst, I can upload a reasonably small CSV so that I can profile its structure.
- Reject unsupported formats and oversize files gracefully.
- Record upload owner, checksum, timestamp and dataset version.
- Never execute arbitrary expressions inside a CSV.

**US-02: Profile data** — As an analyst, I can review column types, missing counts, and duplicate candidates.
- Report inferred types separately from confirmed schema.
- Describe which rows were rejected and why.

**US-03: Validation rules** — As an analyst, I can apply rules for required fields, duplicate keys and numeric/date ranges.
- Rule configuration must be versioned and reviewable.
- Re-running same dataset + same rules yields the same report.

**US-04: Explore data** — As an analyst, I can run *approved read-only* queries over imported datasets.
- Execution occurs using least-privilege database credentials.
- Enforce query timeout, maximum returned rows, dataset isolation and audit log.
- Reject ambiguous or non-select statements; database controls remain primary protection.

**US-05: Report** — As an analyst, I can view a chart/table with calculation metadata.
- Show the dataset version, rule version and the SQL used to obtain the result.
- Exports safely escape spreadsheet-formula content.

**US-06: AI optional** — As an analyst, I may ask for a SQL proposal in natural language.
- Default manual review of query before execution.
- Show generated query and limitations; no fabricated execution.
- Product remains fully usable if AI is disabled.

## Non-functional requirements

- Correctness: idempotent imports, test data fixtures, deterministic transformations.
- Security: authentication, authorization, input validation, least privilege and secure defaults.
- Operability: local Docker Compose development and structured health checks.
- Maintainability: clear boundaries, small PRs, acceptance tests and architecture decisions.
- Accessibility: keyboard-friendly UI, meaningful form errors and readable results.

## Acceptance checkpoint

A user can import `sample_orders.csv`, identify at least two intentional quality defects, safely compute the approved revenue metric and reproduce it after a restart.
