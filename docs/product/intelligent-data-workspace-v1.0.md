# QueryForge v1.0: guided preparation benchmark

Development branch: feat/intelligent-data-workspace-v1.0. Not a verified release.

## Reproduce locally

Run Docker Compose from the branch and open http://127.0.0.1:5080.
Select **Try messy sales data**. Inspect the column profile, then select
the suggested missing-token rules for order_date and revenue and exact
duplicate removal. Under column-specific cleaning rules, add:
- revenue: Number, US format
- order_date: Date, day/month/year (this fixture uses DMY)
- country: Replace PL with Poland
- country: Replace poland with Poland

Preview, verify 8 source rows become 7 and save the cleaned copy.
Calculate sum of revenue grouped by product with quality exclusion off.
Expected: total 2441 PLN; 6 valid revenue records, 1 invalid/missing;
Laptop 1500.50, Monitor 900.25, Mouse 40.25. Keyboard is missing, not zero.
Check that the original dataset still contains eight records.

## Guarantees and limits

- All transformations are previewed and approved through existing ETL lineage.
- Profiles are heuristic; they do not establish business meaning or unique keys.
- Locale policies must be explicitly selected. Do not reapply EU parsing to already
  canonical decimal text; mixed locale columns require separate resolution.
- Unparseable date and number values are preserved, not repaired by guessing.
- Outliers are informational and only calculated on eligible numeric columns.
- Current analytical results skip invalid numeric values; all-missing groups do
  not appear in the ranked numeric output. A missing group is not a zero group.
- Editable files are still capped at 2 MiB, 3000 rows, 40 columns.
- CSV must have consistent field counts and a unique nonempty header row.
- XLSX import reads the first sheet; date/time values become ISO text.
- No new unrestricted code, SQL, external network or automatic currency conversion.
- AI-assisted normalization suggestions, mixed-locale review and time-series analysis
  remain future work.

## Validation

DataPreparationTests.cs covers explicit numeric/date formats, placeholders and
fixture reconciliation. backend-ci.yml has a real API smoke test on the v1.0
branch, including import, profile, recommendation, ETL preview/apply, analysis,
and JavaScript syntax check. Tests must pass before release.
