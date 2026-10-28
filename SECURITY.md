# Security Policy

**Status: prototype, not security-reviewed or production-ready.** Never upload personal, sensitive, confidential, or regulated datasets to a public demo.

## Reporting vulnerabilities

Do not disclose exploitable vulnerabilities in public issues. Contact the maintainer privately through GitHub profile contact details until a dedicated private disclosure process is configured.

## Non-negotiable controls for future releases

- Treat CSV content, prompts, model responses and generated SQL as untrusted.
- Isolate each data workspace and validate tenant/owner access server-side.
- Restrict database execution to least-privilege, read-only roles over approved datasets.
- Enforce statement timeouts, row and resource limits, and audit each execution.
- Protect against CSV injection on exports and injection attacks in API/UI/database interactions.
- Keep secrets out of source control; use environment variables or a secrets manager.
- Design retention/deletion, logging redaction and backup/recovery behavior before production use.
- Use deterministic checks; LLM text is never authorization or validation evidence.

Formal threat modelling and security testing are tracked as milestones; these controls are **requirements, not claims that they are already implemented**.
