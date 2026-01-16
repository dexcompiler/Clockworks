namespace Clockworks.Demo;

internal static class DemoRunner
{
    public static async Task Run(string[] args)
    {
        var demos = DemoCatalog.All;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp(demos);
            return;
        }

        if (args[0] is "list")
        {
            PrintList(demos);
            return;
        }

        var key = args[0];
        if (!demos.TryGetValue(key, out var demo))
        {
            Console.Error.WriteLine($"Unknown demo: '{key}'. Use 'list' to see available demos.");
            return;
        }

        await demo.Invoke([.. args.Skip(1)]);
    }

    private static void PrintHelp(IReadOnlyDictionary<string, Func<string[], Task>> demos)
    {
        Console.WriteLine("Clockworks.Demo");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project demo/Clockworks.Demo -- <demo> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  list                 List available demos");
        Console.WriteLine("  help                 Show this help");
        Console.WriteLine();
        Console.WriteLine("Demos:");
        foreach (var name in demos.Keys.OrderBy(k => k))
        {
            Console.WriteLine($"  {name}");
        }
        Console.WriteLine();
        Console.WriteLine("Note: demos are run explicitly one at a time (no 'all' mode).");
        Console.WriteLine();
    }

    private static void PrintList(IReadOnlyDictionary<string, Func<string[], Task>> demos)
    {
        foreach (var name in demos.Keys.OrderBy(k => k))
        {
            Console.WriteLine(name);
        }
    }
}
