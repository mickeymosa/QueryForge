# QF-004 — Mentor reference (do not merge before completing your attempt)

This branch is a **guided example**, not evidence that you have mastered C#.

## Run locally

Prerequisites: .NET 10 SDK, Git, Visual Studio 2026 or another .NET-capable editor.

```powershell
dotnet --info
dotnet run --project src/QueryForge.Cli/QueryForge.Cli.csproj
dotnet test tests/QueryForge.Domain.Tests/QueryForge.Domain.Tests.csproj
```

Expected console output includes approved, invalid amount, unsupported currency, payment pending, and duplicate ID cases. There are eight xUnit tests. This is an introductory demo, **not a production-ready validation engine**.

## The key files (read in this order)

1. `src/QueryForge.Cli/Program.cs` — input examples and `foreach` loop.
2. `src/QueryForge.Domain/OrderRecord.cs` — a strongly typed immutable record.
3. `src/QueryForge.Domain/OrderValidationResult.cs` — an enum and a result record.
4. `src/QueryForge.Domain/OrderValidator.cs` — `if / else if / else`, `&&`, `!`, `foreach`, `HashSet<T>`.
5. `tests/QueryForge.Domain.Tests/OrderValidatorTests.cs` — unit tests.

## What you must write in your notebook

- Define `string`, `bool`, `decimal` and why `150.50m` uses `m`.
- Explain why `currency != "EUR" && currency != "USD"` is correct.
- Explain **first matching condition wins** and why a zero-valued unpaid order is `InvalidAmount`.
- Describe `OrderRecord`, `OrderValidator`, `Validate`, `ValidateAll` and `OrderValidationResult` in your words.
- Explain why the `HashSet<string>` detects duplicates; compare it to checking a `List<string>` for each item.
- What does `continue` do inside the loop?
- For each test, explain **Arrange / Act / Assert** and what behavior it proves.

## Rebuild exercise

1. Study the mentor branch and close the reference.
2. Create your own branch **from `main`**, e.g. `learner/qf-004-first-attempt`.
3. Recreate the class library, console app, and tests **from memory/notes**, not copy-paste.
4. Start with `Validate` for a single order. Only then implement the `ValidateAll` loop.
5. Commit after each meaningful step and open a PR. Request a review.

## Challenge twists (don't read AI-generated solutions first)

- T1: Accept `eur` and `usd` as well as uppercase codes; write tests.
- T2: Ignore leading/trailing spaces in order IDs when detecting duplicates; write tests.
- T3: Add a rule that `CustomerName` must not be blank, without breaking existing tests.
- T4: Ask what happens if a duplicate order also has a negative amount. Document a deliberate rule precedence.

## Learning evidence checklist

- [ ] I can write a valid `if` condition without reference.
- [ ] I understand records, methods, return values, arguments and enums.
- [ ] I can use `List<T>` and `HashSet<T>` and explain when.
- [ ] I can run, fix and extend unit tests.
- [ ] I can explain why I separated the domain from console I/O.

**Note on scope:** For teaching simplicity, this reference uses case-sensitive currency matching and exact-string duplicate IDs. Real ingestion requires careful normalization, row-level errors, data provenance, internationalization and stronger input limits.
