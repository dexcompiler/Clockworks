using Clockworks;
using Clockworks.Distributed;

namespace Clockworks.Demo.Demos;

internal static class HlcMessagingShowcase
{
    public static Task Run(string[] args)
    {
        Console.WriteLine("HLC message propagation (HTTP/gRPC header style)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        var tp = new SimulatedTimeProvider(DateTimeOffset.UtcNow);

        using var serviceA = new HlcGuidFactory(tp, nodeId: 1);
        using var serviceB = new HlcGuidFactory(tp, nodeId: 2);

        Console.WriteLine("Service A creates an event and sends message to Service B");

        var correlationId = Guid.CreateVersion7();
        var causationId = Guid.CreateVersion7();

        var (msgGuid, sendTs) = serviceA.NewGuidWithHlc();
        var header = new HlcMessageHeader(sendTs, correlationId, causationId);

        var headerValue = header.ToString();
        Console.WriteLine($"  A produced ts: {sendTs}");
        Console.WriteLine($"  A header:     {HlcMessageHeader.HeaderName}: {headerValue}");
        Console.WriteLine($"  A message id: {msgGuid}");
        Console.WriteLine();

        tp.AdvanceMs(20);

        Console.WriteLine("Service B receives header and witnesses timestamp");
        var parsed = HlcMessageHeader.Parse(headerValue);
        serviceB.Witness(parsed.Timestamp);

        var (bGuid, bTs) = serviceB.NewGuidWithHlc();
        Console.WriteLine($"  B after witness ts: {bTs}");
        Console.WriteLine($"  B causal ordering preserved: {sendTs < bTs}");
        Console.WriteLine($"  B message id: {bGuid}");
        Console.WriteLine();

        Console.WriteLine("Decoding GUID back to HLC timestamp:");
        var decoded = bGuid.ToHlcTimestamp();
        Console.WriteLine($"  {bGuid} => {decoded}");

        return Task.CompletedTask;
    }
}
