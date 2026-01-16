using Clockworks.Distributed;
using Microsoft.Data.Sqlite;

namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed class OutboxRepository
{
    private readonly SqliteConnection _conn;

    public OutboxRepository(SqliteStore store)
    {
        _conn = store.Connection;
    }

    public async Task EnqueueAsync(string destination, Guid messageId, string messageType, string json, HlcMessageHeader header, long nowMs, long availableMs, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO outbox(id, destination, message_type, json, hlc_header, created_utc_ms, attempts, available_utc_ms)
VALUES ($id, $dest, $type, $json, $hlc, $created, 0, $avail);
";
        cmd.Parameters.AddWithValue("$id", messageId.ToString("N"));
        cmd.Parameters.AddWithValue("$dest", destination);
        cmd.Parameters.AddWithValue("$type", messageType);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$hlc", header.ToString());
        cmd.Parameters.AddWithValue("$created", nowMs);
        cmd.Parameters.AddWithValue("$avail", availableMs);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxRow>> DequeueAvailableAsync(long nowMs, int max, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, destination, message_type, json, hlc_header, attempts, available_utc_ms
FROM outbox
WHERE available_utc_ms <= $now
ORDER BY available_utc_ms ASC
LIMIT $max;
";
        cmd.Parameters.AddWithValue("$now", nowMs);
        cmd.Parameters.AddWithValue("$max", max);

        var rows = new List<OutboxRow>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new OutboxRow(
                Id: Guid.ParseExact(reader.GetString(0), "N"),
                Destination: reader.GetString(1),
                MessageType: reader.GetString(2),
                Json: reader.GetString(3),
                HlcHeader: reader.GetString(4),
                Attempts: reader.GetInt64(5),
                AvailableUtcMs: reader.GetInt64(6)));
        }
        return rows;
    }

    public async Task DeleteAsync(Guid messageId, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM outbox WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", messageId.ToString("N"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RescheduleAsync(Guid messageId, long attempts, long nextAvailableMs, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
UPDATE outbox
SET attempts = $attempts, available_utc_ms = $avail
WHERE id = $id;
";
        cmd.Parameters.AddWithValue("$id", messageId.ToString("N"));
        cmd.Parameters.AddWithValue("$attempts", attempts);
        cmd.Parameters.AddWithValue("$avail", nextAvailableMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public sealed record OutboxRow(
        Guid Id,
        string Destination,
        string MessageType,
        string Json,
        string HlcHeader,
        long Attempts,
        long AvailableUtcMs);
}
