# Engineering Mentorship Agreement

## Roles

**Abdelrahman: owner / developer** — writes the first version of core domain logic; designs database schemas; implements security boundaries; writes or at least meaningfully authors tests; explains code and owns merges/releases.

**AI: mentor / reviewer / scaffolding assistant** — explains concepts; breaks work into sized issues; generates low-risk boilerplate/document templates; reviews PRs; proposes test cases and adversarial examples; diagnoses issues after a human debugging attempt; creates learning checkpoints.

## The learning loop

1. **Explain** the requirement in your own words.
2. **Design** an approach, including risks and test cases.
3. **Implement** a small feature without copying a generated final solution.
4. **Test** happy path, bad inputs and boundary conditions.
5. **Review** with AI; evaluate suggestions, never accept them blindly.
6. **Defend**: describe design choices, debug a modified case and identify tradeoffs.

## Assistance levels

- A0: no help; timed interview challenge.
- A1: conceptual hint or reference links only.
- A2: pseudocode, unit test suggestions, high-level architecture review.
- A3: targeted correction or pair debugging (record what was done).
- A4: full implementation or generated boilerplate (only for non-core/repetitive tasks).

Record the assistance level on each substantial PR. Core engineering skills target A0–A2 by final review.

## Evidence standard

A skill progresses from **familiar → guided → independent → advanced practical** only when supported by code, tests, explanation and a novel problem solved independently. One feature alone never proves advanced mastery of an entire language or discipline.

## Budget controls

One repo, one planning board, few dependencies, modular monolith first, local-first services, no paid AI required, weekly timeboxing. Reuse vetted libraries rather than recreating general-purpose tooling without a learning reason.
