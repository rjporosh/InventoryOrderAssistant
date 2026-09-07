using System.ComponentModel;
using System.Text.Json;

namespace InventoryOrderAssistant.Database;

/// <summary>The three tools the agent uses to explore the schema and read data.</summary>
public sealed class SqlReadOnlyTools
{
    private readonly SqlServerService _database;

    public SqlReadOnlyTools(SqlServerService database) => _database = database;

    [Description("Lists all user tables (schema.table). Call this first when the schema is unknown.")]
    public async Task<string> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = await _database.GetTablesAsync(cancellationToken);
        return tables.Count == 0
            ? "No tables found."
            : string.Join(Environment.NewLine, tables);
    }

    [Description("Describes a table's columns (name | type | nullable). Call before writing SQL against it.")]
    public async Task<string> DescribeTableAsync(
        [Description("Schema name, normally dbo")] string schema,
        [Description("Table name")] string table,
        CancellationToken cancellationToken = default)
    {
        var columns = await _database.DescribeTableAsync(schema, table, cancellationToken);
        return columns.Count == 0
            ? $"Table {schema}.{table} was not found."
            : string.Join(Environment.NewLine,
                columns.Select(c => $"{c.Name} | {c.DataType} | Nullable: {c.IsNullable}"));
    }

    [Description(
        "Runs exactly one read-only SELECT (or WITH) query and returns rows as JSON. " +
        "INSERT/UPDATE/DELETE/DDL and multiple statements are rejected. Results are capped.")]
    public async Task<string> ExecuteQueryAsync(
        [Description("A single read-only SQL query")] string sql,
        CancellationToken cancellationToken = default)
    {
        var rows = await _database.ExecuteReadOnlyAsync(sql, cancellationToken);
        return rows.Count == 0
            ? "Query returned zero rows."
            : JsonSerializer.Serialize(rows);
    }
}
