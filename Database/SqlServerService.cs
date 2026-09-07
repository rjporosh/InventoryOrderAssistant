using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace InventoryOrderAssistant.Database;

/// <summary>Read-only access to the inventory database. Never opens a writable command.</summary>
public sealed class SqlServerService
{
    private static readonly Regex ForbiddenKeyword = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _connectionString;
    private readonly int _commandTimeoutSeconds;
    private readonly int _maxRows;

    public SqlServerService(DatabaseOptions options)
    {
        _connectionString = options.ConnectionString;
        _commandTimeoutSeconds = options.CommandTimeoutSeconds;
        _maxRows = options.MaxRows;
    }

    /// <summary>Verifies the database is reachable. Throws a readable error if it is not.</summary>
    public async Task EnsureConnectableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot connect to the inventory database. " +
                "Check the server is running and 'Database:ConnectionString' is correct. " +
                $"Underlying error: {ex.Message}", ex);
        }
    }

    public async Task<List<string>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TABLE_SCHEMA + '.' + TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME;
            """;

        var result = new List<string>();
        await using var reader = await OpenReaderAsync(sql, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<List<ColumnInfo>> DescribeTableAsync(
        string schema, string table, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;

        var result = new List<ColumnInfo>();
        await using var reader = await OpenReaderAsync(
            sql, cancellationToken,
            new SqlParameter("@schema", schema),
            new SqlParameter("@table", table));

        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<List<Dictionary<string, object?>>> ExecuteReadOnlyAsync(
        string sql, CancellationToken cancellationToken = default)
    {
        ValidateReadOnlyQuery(sql);

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await OpenReaderAsync(sql, cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= _maxRows)
                break;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    private async Task<SqlDataReader> OpenReaderAsync(
        string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = new SqlCommand(sql, connection) { CommandTimeout = _commandTimeoutSeconds };
        command.Parameters.AddRange(parameters);

        // Closes the connection when the reader is disposed.
        return await command.ExecuteReaderAsync(
            System.Data.CommandBehavior.CloseConnection, cancellationToken);
    }

    private static void ValidateReadOnlyQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL query cannot be empty.");

        var normalized = sql.Trim().TrimEnd(';').Trim();

        if (normalized.Contains(';'))
            throw new InvalidOperationException("Multiple SQL statements are not allowed.");

        if (!normalized.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only SELECT / WITH queries are allowed.");

        var match = ForbiddenKeyword.Match(normalized);
        if (match.Success)
            throw new InvalidOperationException(
                $"Forbidden SQL keyword detected: {match.Value}");
    }
}

public sealed record DatabaseOptions
{
    public string ConnectionString { get; init; } = "";
    public int CommandTimeoutSeconds { get; init; } = 10;
    public int MaxRows { get; init; } = 200;
}

public sealed record ColumnInfo(string Name, string DataType, string IsNullable);
