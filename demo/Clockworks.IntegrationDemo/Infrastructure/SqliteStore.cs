using Microsoft.Data.Sqlite;

namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed class SqliteStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqliteStore> OpenAsync(string connectionString, CancellationToken ct)
    {
        var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);

        var store = new SqliteStore(conn);
        await store.InitializeAsync(ct);
        return store;
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        // Outbox: messages produced by this node to be delivered.
        // Inbox: messages received; used to ensure idempotency.
        // Orders: simple workflow state.
        var sql = @"
CREATE TABLE IF NOT EXISTS outbox (
    id TEXT PRIMARY KEY,
    destination TEXT NOT NULL,
    message_type TEXT NOT NULL,
    json TEXT NOT NULL,
    hlc_header TEXT NOT NULL,
    created_utc_ms INTEGER NOT NULL,
    attempts INTEGER NOT NULL,
    available_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS inbox (
    id TEXT PRIMARY KEY,
    message_type TEXT NOT NULL,
    hlc_header TEXT NOT NULL,
    received_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS orders (
    order_id TEXT PRIMARY KEY,
    customer_id TEXT NOT NULL,
    amount REAL NOT NULL,
    status TEXT NOT NULL,
    last_hlc TEXT NOT NULL
);
";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public SqliteConnection Connection => _connection;

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
