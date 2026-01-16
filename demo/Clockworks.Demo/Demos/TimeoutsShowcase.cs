using Clockworks.Instrumentation;

namespace Clockworks.Demo.Demos;

internal static class TimeoutsShowcase
{
    public static async Task Run(string[] args)
    {
        Console.WriteLine("Timeouts with TimeProvider (fast-forwardable timeouts)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        var tp = SimulatedTimeProvider.FromEpoch();
        var stats = new TimeoutStatistics();

        Console.WriteLine("Scheduling 3 timeouts at different times...");

        using var h1 = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(1), stats);
        using var h2 = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(3), stats);
        using var h3 = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(5), stats);

        Console.WriteLine($"Created: {stats.Created}, Fired: {stats.Fired}, Disposed: {stats.Disposed}");

        // Await a task that completes when each token cancels.
        var tasks = new (string Name, CancellationToken Token)[]
        {
            ("t+1s", h1.Token),
            ("t+3s", h2.Token),
            ("t+5s", h3.Token),
        }
        .Select(x => WaitForCancellation(x.Name, x.Token))
        .ToArray();

        Console.WriteLine("Advancing simulated time in steps...");
        for (int i = 0; i < 6; i++)
        {
            tp.Advance(TimeSpan.FromSeconds(1));
            Console.WriteLine($"  now={tp.GetUtcNow():O}  Created={stats.Created} Fired={stats.Fired} Disposed={stats.Disposed}");
        }

        await Task.WhenAll(tasks);

        Console.WriteLine();
        Console.WriteLine("Demonstrating cancellation by user before timeout:");
        stats.Reset();

        using var handle = Timeouts.CreateTimeoutHandle(tp, TimeSpan.FromSeconds(10), stats);
        handle.Dispose();
        Console.WriteLine($"After dispose: Created={stats.Created} Fired={stats.Fired} Disposed={stats.Disposed}");

        tp.Advance(TimeSpan.FromSeconds(20));
        Console.WriteLine($"After advancing past due: Created={stats.Created} Fired={stats.Fired} Disposed={stats.Disposed}");
    }

    private static async Task WaitForCancellation(string name, CancellationToken token)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"  token '{name}' cancelled");
        }
    }
}
