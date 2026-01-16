using Clockworks;

namespace Clockworks.Demo.Demos;

internal static class UuidV7Sortability
{
    public static Task Run(string[] args)
    {
        Console.WriteLine("UUIDv7 Sortability (lexicographic & temporal ordering)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        var tp = new SimulatedTimeProvider(DateTimeOffset.UnixEpoch);
        using var f = new UuidV7Factory(tp, overflowBehavior: CounterOverflowBehavior.IncrementTimestamp);

        // Force some IDs in same ms and across ms, then show ordering properties.
        Console.WriteLine("Generating IDs (some within same millisecond)...");
        var ids = new List<Guid>();

        ids.Add(f.NewGuid());
        ids.Add(f.NewGuid());
        ids.Add(f.NewGuid());

        tp.AdvanceMs(1);
        ids.Add(f.NewGuid());

        tp.AdvanceMs(10);
        ids.Add(f.NewGuid());

        Console.WriteLine();
        Console.WriteLine("In generation order:");
        foreach (var id in ids)
        {
            Console.WriteLine($"  {id}  ts={id.GetTimestampMs()} ctr={id.GetCounter()}");
        }

        Console.WriteLine();
        Console.WriteLine("Sorted by Guid.CompareTo (should match time order for v7):");
        var sorted = ids.OrderBy(g => g).ToArray();
        foreach (var id in sorted)
        {
            Console.WriteLine($"  {id}  ts={id.GetTimestampMs()} ctr={id.GetCounter()}");
        }

        Console.WriteLine();
        Console.WriteLine("Pairwise checks:");
        for (int i = 1; i < sorted.Length; i++)
        {
            var prev = sorted[i - 1];
            var next = sorted[i];
            Console.WriteLine($"  sorted[{i - 1}] < sorted[{i}] : {prev < next}  (CompareByTimestamp={prev.CompareByTimestamp(next)})");
        }

        Console.WriteLine();
        Console.WriteLine("Note: UUIDv7 is designed to be sortable by bytes (big-endian). .NET Guid comparison aligns with this ordering.");

        return Task.CompletedTask;
    }
}
