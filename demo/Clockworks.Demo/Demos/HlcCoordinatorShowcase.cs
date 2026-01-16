using Clockworks;
using Clockworks.Distributed;

namespace Clockworks.Demo.Demos;

internal static class HlcCoordinatorShowcase
{
    public static Task Run(string[] args)
    {
        Console.WriteLine("HlcCoordinator (BeforeSend/BeforeReceive workflow)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        var tp = new SimulatedTimeProvider(DateTimeOffset.UtcNow);

        using var aFactory = new HlcGuidFactory(tp, nodeId: 1);
        using var bFactory = new HlcGuidFactory(tp, nodeId: 2);

        var a = new HlcCoordinator(aFactory);
        var b = new HlcCoordinator(bFactory);

        Console.WriteLine("A sends a message to B...");
        var sendTs = a.BeforeSend();
        Console.WriteLine($"  A send timestamp: {sendTs}");

        tp.AdvanceMs(5);

        Console.WriteLine("B receives message...");
        b.BeforeReceive(sendTs);
        Console.WriteLine($"  B current timestamp after receive: {b.CurrentTimestamp}");

        var bEvent = b.NewLocalEventGuid();
        Console.WriteLine($"  B emits local event guid: {bEvent}");
        Console.WriteLine($"  B event decodes to: {bEvent.ToHlcTimestamp()}");

        Console.WriteLine();
        Console.WriteLine("Coordinator statistics:");
        Console.WriteLine($"  A: {a.Statistics}");
        Console.WriteLine($"  B: {b.Statistics}");

        return Task.CompletedTask;
    }
}
