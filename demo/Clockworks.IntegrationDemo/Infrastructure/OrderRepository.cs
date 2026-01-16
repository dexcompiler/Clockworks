using Microsoft.Data.Sqlite;

namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed class OrderRepository
{
    private readonly SqliteConnection _conn;

    public OrderRepository(SqliteStore store)
    {
        _conn = store.Connection;
    }

    public async Task UpsertPlacedAsync(Guid orderId, string customerId, decimal amount, string lastHlc, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO orders(order_id, customer_id, amount, status, last_hlc)
VALUES ($id, $cust, $amount, 'Placed', $hlc)
ON CONFLICT(order_id) DO UPDATE SET
  customer_id = excluded.customer_id,
  amount = excluded.amount,
  status = 'Placed',
  last_hlc = excluded.last_hlc;
";
        cmd.Parameters.AddWithValue("$id", orderId.ToString("N"));
        cmd.Parameters.AddWithValue("$cust", customerId);
        cmd.Parameters.AddWithValue("$amount", (double)amount);
        cmd.Parameters.AddWithValue("$hlc", lastHlc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid orderId, string status, string lastHlc, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
UPDATE orders SET status = $status, last_hlc = $hlc WHERE order_id = $id;
";
        cmd.Parameters.AddWithValue("$id", orderId.ToString("N"));
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$hlc", lastHlc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<OrderRow?> GetAsync(Guid orderId, CancellationToken ct)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT order_id, customer_id, amount, status, last_hlc
FROM orders
WHERE order_id = $id;
";
        cmd.Parameters.AddWithValue("$id", orderId.ToString("N"));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new OrderRow(
            OrderId: Guid.ParseExact(reader.GetString(0), "N"),
            CustomerId: reader.GetString(1),
            Amount: (decimal)reader.GetDouble(2),
            Status: reader.GetString(3),
            LastHlc: reader.GetString(4));
    }

    public sealed record OrderRow(Guid OrderId, string CustomerId, decimal Amount, string Status, string LastHlc);
}
