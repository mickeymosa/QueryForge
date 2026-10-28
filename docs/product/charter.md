# QueryForge — Product Charter (Draft v0.1)

## Vision

Help small teams trust their data by making validation, provenance and reproducible analysis accessible without expensive software or mandatory AI.

## Initial target user

A technically curious analyst, student organization or small nonprofit that handles operational CSV exports but lacks a dedicated data engineering team.

## Primary job to be done

"When I receive a CSV report, I want to know what is wrong with the data and how a reported number was calculated, so I can act without guessing."

## Assumptions to validate

- Users lose time on duplicate records, inconsistent columns, missing values and undocumented calculations.
- A local-first, self-hostable interface is valuable to at least some users.
- Transparency and reproducibility matter more than flashy AI answers.

These are hypotheses, not proven user needs. Interview at least 3 potential users before major feature expansion.

## Out of scope for MVP

- Direct credentials to arbitrary live production databases.
- Multi-tenant SaaS or regulated/confidential data handling.
- Autonomous write-capable AI agents or execution of arbitrary model-generated SQL.
- Scheduling, distributed processing, marketplace connectors and advanced collaboration.

## Success measures

- New developer can follow README to run the local MVP.
- Example dirty CSV import generates an auditable quality report.
- A user can reproduce an aggregate result from the same dataset version and rules.
- Test suite includes negative access-control cases and corrupted inputs.
- A non-technical reviewer can trace a number to its input and calculation.

## Constraints

Budget: €0 software subscriptions for local development. Public hosting is optional and limited by provider policies. Build correctness and security ahead of AI feature breadth.
