# ADR-0001: Start with modular ASP.NET Core API, not microservices

- Status: **Proposed**
- Date: 2026-10-28
- Decision owner: Abdelrahman Awadalla

## Context

The project must deliver a testable, usable MVP with zero mandatory paid infrastructure while maximizing C#/.NET learning. A multi-service deployment would add integration and operations overhead early.

## Proposal

Start with one ASP.NET Core backend organized by responsibility: domain, application logic, infrastructure and HTTP API. PostgreSQL stores application/workspace metadata and imported analysis data. Introduce a Python ingestion worker only once a real Python-specific processing need is demonstrated. Build a React/TypeScript UI after the first API vertical slice is tested.

## Consequences

**Advantages:** simpler local debugging, easier integration testing, clear ownership of business rules, fewer moving parts and faster feedback.

**Costs:** explicit boundaries are still needed, and isolated execution for untrusted data may later require separate processes or containers.

## Security notes

The initial server must not accept live production database credentials or arbitrary query execution. An LLM query proposal is untrusted until checked against allowlisted datasets and enforced by database permissions and resource limits.

## Review trigger

Revisit only after the ingestion/query workload demonstrates a concrete need for background service separation.
