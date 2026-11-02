# QueryForge Backend: Engineering Review & Release Gates
**Status:** BLOCKED FOR PUBLIC RELEASE — 2026-11-02
**Reviewed PR:** #12 (draft); this is a technical review, not a merge approval.

## Product promise and architecture

QueryForge aims to provide a *simple* upload → validate → explore experience. Behind this interface, the system must enforce explicit schemas, authorization, reproducible data-quality rules, source provenance and explainable computations.

- **C# / ASP.NET Core** handles HTTP boundaries, permissions and application orchestration. Why: typed contracts and enterprise-ready backend tooling.
- **Records and interfaces** such as `QualityReport` and `IQualityProfiler` separate *what the system means* from *how it is implemented*. Why: testability and replaceability. **Clarification:** a C# `record` is a language construct; abstract data types describe interfaces/behavior, not an automatic way to generate a full-stack UI.
- **Deterministic validation** runs before optional AI. Why: model output is untrusted and cannot establish data correctness.
- **PostgreSQL + EF Core** support durable data and eventual migrations. Why: relational constraints, transactions and queryability.
- **Simple UI** hides complexity from the user, not from our engineering review. Why: accessible software should not require SQL expertise.
- **Docker + CI** check repeatability. Why: code that runs on one developer computer is not enough.

## Current verified evidence

[GitHub Actions run #41493511767](https://github.com/mickeymosa/QueryForge/actions/runs/41493511767) passed .NET build and tests, Docker Compose startup, PostgreSQL health, sample import, validation and amount metric. These checks do NOT establish production safety, large-file resilience, usability or robustness of external integration.

## Blockers discovered in the actual code

| Area | Evidence | Risk | Required decision and proof |
|---|---|---|---|
| Authentication / isolation | `Program.cs` exposes dataset list, metadata, preview and metrics without credentials | Every reachable client may read data | Authenticate users, authorize every dataset operation, test cross-user access and IDOR |
| Upload CSRF / request boundaries | `MapPost(...).DisableAntiforgery()` in `Program.cs` | Unsafe for browser/session deployment | Introduce a suitable CSRF and authentication strategy; add adversarial tests |
| Schema lifecycle | `EnsureCreatedAsync()` in startup | No controlled upgrade/rollback path | Use EF Core migrations, transactionally preserve old data, test applying migrations from previous versions |
| Persistence design | `RowsJson` and `ReportJson` in `Persistence.cs` | Entire datasets deserialized on read; difficult indexing and versioning | Normalized dataset versions and row storage, or a justified analysis store with bounded paging |
| CSV parsing and data policies | `CsvImportService.cs` and `Domain.cs` | Heuristics (`id`, `amount`, first currency) can silently impose incorrect business rules | Strict malformed-record tests, configurable schema and user-confirmed currency/duplicate policies |
| Numeric reporting | `Summarize` excludes all flagged rows and accepts any parsable selected column | Aggregates can be misleading and ignore business semantics | Typed metric definitions, exclusion reasons, expected units and test datasets with known answers |
| Security and storage | Compose uses local demo credentials; no retention, deletion, quotas per user | Dangerous public deployment; uncontrolled data retention | Secure secret management, deletion/retention, bounded resource usage and threat model |
| UI and accessibility | Static HTML/JS upload screen with no verified browser tests | Friction for nontechnical users and hidden errors | Keyboard/screen-reader coverage, loading/error/empty states, user tests |
| Operational quality | CI checks one small example end-to-end | Failure scenarios remain unknown | Recovery, cancellation, restart, DB outage, concurrency and load-test evidence |

## Merge gates — ALL mandatory for a release-candidate PR

1. **Show the change:** A concise diff overview with affected files and an end-to-end feature walkthrough.
2. **Explain why:** Problem, design alternatives, the selected solution and trade-offs (ADR for structural decisions).
3. **Prove behavior:** Unit + API/integration + Docker smoke tests relevant to the feature, including negative scenarios.
4. **Show robustness:** Invalid inputs, permission boundaries, timeout/restart behavior, resource budgets and observability.
5. **Show usability:** A demo of the upload → quality explanation → exploration path using a nontechnical user's perspective.
6. **Explain it to the owner:** Live-style review questions and a human-readable learning guide.
7. **Owner sign-off:** Abdelrahman explicitly confirms the reviewed behavior, risks and remaining work. No AI self-approval/auto-merge.

For a **public, production-oriented v1.0**, all critical/high findings must be resolved, deployment/security documentation must be accurate, and a release checklist must be completed. A local-only teaching prototype can remain on a draft branch while those blockers are addressed.

## Definition of Done for individual engineering tickets

- Requirements and negative acceptance criteria agreed
- Design and technical rationale recorded
- Code and tests are reviewable in a small PR
- CI green on the final commit
- Relevant failure tests and security checks pass
- Documentation updated, with a learning/defense exercise
- Owner reviews evidence and approves the merge

## Implementation order (value first)

1. Product contract + authentication/authorization boundary + threat model.
2. Dataset versioning, migrations, safe storage and data lifecycle.
3. Strict schema / CSV validation and explicit metric semantics.
4. Integration/contract/e2e testing and observability.
5. Improved simple UI and optional AI only once deterministic analysis is trustworthy.

Do not call a feature "complete" solely because code was generated or one happy-path test passed.
