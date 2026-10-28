# Sprint 0 — Create the professional foundation

**Timebox:** approximately 15 hours. No substantial product features this sprint.

| Task | Ownership | Estimate | Acceptance evidence |
| --- | --- | ---: | --- |
| Define user persona + interview prompts | Human leads / AI facilitates | 2h | Charter updated with assumptions |
| Turn vision into 6 MVP stories | Human defines / AI formats | 2h | Requirements approved |
| Create repository and first issue backlog | AI scaffolds / human reviews | 2h | Public repo and linked issues |
| Prepare laptop toolchain (.NET, VS, Git, Docker, Python) | Human hands-on | 3h | Command outputs + screenshot notes, no secrets |
| Model first entities and relationships | Human leads / AI reviews | 2h | First ERD and explanation |
| C# domain warm-up: `DatasetRecord` | Human codes / AI reviews | 3h | Working tests, self-explanation |
| Sprint retrospective | Human | 1h | Lessons, blockers and next priority |

## First learner-owned programming exercise

Create a C# project with a `DatasetRecord` representing order ID, customer ID, order date, currency, and amount. Given a supplied list of records, implement a service that flags duplicate order IDs and invalid amounts. Keep validation separate from input/output. Write unit tests for: valid rows, duplicates, negative amounts, and empty input.

**Do not ask AI to write the first implementation.** It may explain C# concepts, critique the plan, and review a PR afterward.

## Review questions

1. Why did you choose class or record for `DatasetRecord`?
2. Which responsibilities belong in domain versus API code?
3. Which test would fail if duplicate detection were incorrect?
4. How would you prevent a null value or malformed amount from crashing the application?
5. What would change if the dataset contained one million records?
