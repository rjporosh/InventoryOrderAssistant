# Inventory & Order Assistant

A single AI agent that answers plain-language questions about an inventory / order
database. It does **not** know the schema up front — it runs a loop:

```
list_tables  ->  describe_table  ->  execute_query  ->  (repeat)  ->  answer
```

No sub-agents, no orchestrator. One agent, one loop.

## Project layout

| Path | Purpose |
|------|---------|
| `Agent/InventoryAgent.cs`      | The tool-calling loop and system prompt. |
| `Database/SqlServerService.cs` | Read-only SQL Server access + guardrails. |
| `Database/SqlReadOnlyTools.cs` | The 3 tools exposed to the model. |
| `Database/schema.sql`          | Creates `InventoryDb`, seeds data, creates the read-only login. |
| `Program.cs`                   | Config, startup checks, REPL. |
| `appsettings.json`             | Connection string, timeouts, model. |

## Guardrails

- Login `inventory_reader` is `db_datareader` only — no writes, no DDL (`schema.sql`).
- App-level check: query must start with `SELECT`/`WITH`, single statement, no
  `INSERT/UPDATE/DELETE/DROP/ALTER/CREATE/...` keywords (`SqlServerService.ValidateReadOnlyQuery`).
- Command timeout on every query (`Database:CommandTimeoutSeconds`, default 10s).
- Row cap on results (`Database:MaxRows`, default 200).
- Tool errors are returned to the model, not thrown — it retries a better query.

## Setup

### 1. Database

```bash
sqlcmd -S localhost -U sa -P "<sa-password>" -i Database/schema.sql
```

Any SQL Server works (local, Docker, Azure SQL). Docker example:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_strong_Pass1" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Configuration

Edit `appsettings.json` (or create `appsettings.local.json`, git-ignored, with the
same shape) so `Database:ConnectionString` points at your server.

```bash
export OPENAI_API_KEY="sk-..."
```

### 3. Run

```bash
dotnet run
```

```
> Which products have fewer than 10 units in stock?
USB-C Cable 1m (6), Mechanical Keyboard (3), Laptop Stand (9).

> How many orders has Rafi Ahmed placed, and what's the total value?
Rafi Ahmed has placed 3 orders totalling $110.50.

> exit
```

## Sample questions

- Which products have fewer than 10 units in stock?
- How many orders has customer *[name]* placed, and what's the total value?
- What were total sales in the last 7 days?
- List customers who haven't ordered anything in the last 60 days.

## Configuration reference

| Key | Default | Meaning |
|-----|---------|---------|
| `Database:ConnectionString`     | localhost / InventoryDb | Target database. |
| `Database:CommandTimeoutSeconds`| `10` | Per-query timeout. |
| `Database:MaxRows`              | `200` | Max rows returned to the model. |
| `Agent:Model`                   | `gpt-4.1` | OpenAI chat model. |
| `Agent:MaxIterations`           | `10` | Max loop turns before giving up. |
| `OPENAI_API_KEY` (env)          | — | Required. |

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `OPENAI_API_KEY environment variable is not set` | `export OPENAI_API_KEY=...` |
| `Cannot connect to the inventory database` | Start SQL Server / fix the connection string. Message includes the underlying error. |
| `Only SELECT / WITH queries are allowed` | The model tried a non-read query; it will retry. Persistent = tighten the prompt. |
| `Agent stopped after N iterations` | Raise `Agent:MaxIterations` or simplify the question. |
