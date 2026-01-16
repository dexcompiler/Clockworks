using Microsoft.Data.Sqlite;

namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed class InboxRepository
{
    private readonly SqliteConnection _conn;

    public InboxRepository(SqliteStore store)
    {
        _conn = store.Connection;
    }

    public async Task<bool> TryMarkReceivedAsync(Guid messageId, string messageType, string hlcHeader, long nowMs, CancellationToken ct)
    {
        // INSERT OR IGNORE gives idempotency.
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR IGNORE INTO inbox(id, message_type, hlc_header, received_utc_ms)
VALUES ($id, $type, $hlc, $now);
SELECT changes();
";
        cmd.Parameters.AddWithValue("$id", messageId.ToString("N"));
        cmd.Parameters.AddWithValue("$type", messageType);
        cmd.Parameters.AddWithValue("$hlc", hlcHeader);
        cmd.Parameters.AddWithValue("$now", nowMs);

        var changed = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return changed > 0;
    }
}
