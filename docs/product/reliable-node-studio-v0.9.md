# v0.9 — reliable local node studio (draft)

Status: draft PR, not production-ready. Latest tested integration gates must be green before release.

## What changes
- Free-position ETL nodes via the ⠿ grip on the Studio canvas; per-dataset browser-local draft layouts.
- Drag an operation from the left palette onto the canvas. Click output → input ports to change the *single, real* execution order. Joins, parallel branches and multi-input DAGs are not supported, and the UI does not simulate them.
- Advanced Inspector controls the real, server-executed operation. The `KeepColumns` step takes comma-separated exact field names in "New value / target"; e.g. `order_id,amount,currency`.
- Preview executes the deterministic C# pipeline; Apply requires a matching preview fingerprint and saves a new derived dataset, leaving the original untouched.
- Export the currently selected editable dataset as a CSV file. Spreadsheet formula-like cell values are prefixed with a literal apostrophe. Data remains local.
- Optional local Ollama or hosted OpenAI text-to-ETL proposals. Neither can execute arbitrary code or write without approval.

## Run
```powershell
git fetch origin
git switch --track origin/feat/reliable-node-studio-v0.9
docker compose up --build -d
```
Open http://127.0.0.1:5080, click **Try sample data**, then **Prepare & ETL**. In Advanced Mode, place/drag nodes; to adjust execution order, click a node's right output circle then the next node's left input circle. Preview then Apply. Export from the new selected dataset.

## AI configuration

### Option A: Ollama (on your PC, no paid API)
```powershell
ollama pull llama3.2
$env:QUERYFORGE_OLLAMA_MODEL="llama3.2"
$env:QUERYFORGE_AI_PROVIDER="ollama"
docker compose up --build -d
```
Ollama must be running and reachable as `host.docker.internal:11434` from Docker.

### Option B: Hosted OpenAI API (QueryForge and data remain local)
```powershell
$env:QUERYFORGE_AI_PROVIDER="openai"
$env:QUERYFORGE_AI_MODEL="gpt-4.1-mini"
$env:QUERYFORGE_AI_API_KEY="YOUR_OPENAI_API_KEY"
docker compose up --build -d
```
The .NET backend calls the fixed HTTPS OpenAI endpoint `https://api.openai.com/v1/chat/completions`. The API key is sent as a bearer token from the backend. **Never put the key in frontend code, screenshots, GitHub, committed `.env` files or logs.** Prefer a locally configured secret store instead of retaining keys in shell history. Provider usage may be billable.

Only the user's instruction and source column names (not dataset records) are submitted to the provider. Do not include personal/confidential values in prompts or column headers. Cloud usage requires intentional user consent. Model output is parsed as JSON, limited to allowed transformations, validated by C# against the real dataset schema, previewed, and never automatically saved.

For Ollama and OpenAI modes, a user can always choose the fully offline guided commands instead.

## Current limitations
- The free-position canvas executes **one sequential path**, not branching graphs or joins.
- Editable dataset limits remain 2 MiB / 3,000 rows / 40 columns. The 25 MiB CSV route remains read-only.
- No login, multi-user authorization, hardened XLSX sandbox or independent data-processing worker. Never expose the local API publicly or upload confidential data.
- Browser-local drafts save workflow text and canvas locations only, not source rows; clearing storage deletes drafts. The final approved transform still persists as a new dataset.
- Export is available only for editable datasets, not large read-only v2 imports.
- This branch must pass tests/CI and a hands-on usability review before being called a v0.9 release.
