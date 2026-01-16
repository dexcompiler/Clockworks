using Clockworks.Distributed;

namespace Clockworks.IntegrationDemo.Domain;

public sealed record PlaceOrderRequest(string CustomerId, decimal Amount);

public sealed record OrderPlaced(Guid OrderId, string CustomerId, decimal Amount);

public sealed record PaymentReserved(Guid OrderId, bool Success);

public sealed record InventoryAllocated(Guid OrderId, bool Success);

public sealed record OrderConfirmed(Guid OrderId);

public sealed record MessageEnvelope(
    Guid MessageId,
    string MessageType,
    string Json,
    HlcMessageHeader Header);
