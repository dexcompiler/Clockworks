using Clockworks;
using Clockworks.Instrumentation;

namespace Clockworks.Demo.Demos;

internal static class SimulatedTimeProviderShowcase
{
    public static Task Run(string[] args)
    {
        Console.WriteLine("SimulatedTimeProvider (timers, coalescing, statistics)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        var tp = SimulatedTimeProvider.FromEpoch();
        tp.Statistics.Reset();

        var fired = new List<string>();

        using var t1 = tp.CreateTimer(_ => fired.Add("t1@+2s"), state: null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        using var t2 = tp.CreateTimer(_ => fired.Add("t2@+1s"), state: null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var p = tp.CreateTimer(_ => fired.Add("p@+1s(periodic)"), state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Console.WriteLine("Advance +1s (should fire t2 and first periodic tick):");
        tp.Advance(TimeSpan.FromSeconds(1));
        Dump(fired);

        Console.WriteLine("Advance +10s (periodic coalesces; only one tick per advance call):");
        tp.Advance(TimeSpan.FromSeconds(10));
        Dump(fired);

        Console.WriteLine();
        Console.WriteLine("Statistics:");
        PrintStats(tp.Statistics);

        Console.WriteLine();
        Console.WriteLine("Changing periodic to one-shot at +5s from now:");
        p.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        tp.Advance(TimeSpan.FromSeconds(5));
        Dump(fired);

        Console.WriteLine();
        Console.WriteLine("Final statistics:");
        PrintStats(tp.Statistics);

        return Task.CompletedTask;
    }

    private static void Dump(List<string> fired)
    {
        Console.WriteLine($"  fired: [{string.Join(", ", fired)}]");
    }

    private static void PrintStats(SimulatedTimeProviderStatistics s)
    {
        Console.WriteLine($"  TimersCreated:     {s.TimersCreated}");
        Console.WriteLine($"  TimerChanges:      {s.TimerChanges}");
        Console.WriteLine($"  TimersDisposed:    {s.TimersDisposed}");
        Console.WriteLine($"  CallbacksFired:    {s.CallbacksFired}");
        Console.WriteLine($"  PeriodicSchedules: {s.PeriodicReschedules}");
        Console.WriteLine($"  AdvanceCalls:      {s.AdvanceCalls}");
        Console.WriteLine($"  MaxQueueLength:    {s.MaxQueueLength}");
        Console.WriteLine($"  QueueEnqueues:     {s.QueueEnqueues}");
    }
}
