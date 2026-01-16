using Clockworks.Demo.Demos;

namespace Clockworks.Demo;

internal static class DemoCatalog
{
    public static IReadOnlyDictionary<string, Func<string[], Task>> All { get; } =
        new Dictionary<string, Func<string[], Task>>(StringComparer.OrdinalIgnoreCase)
        {
            ["uuidv7"] = UuidV7Showcase.Run,
            ["uuidv7-sortability"] = UuidV7Sortability.Run,
            ["timeouts"] = TimeoutsShowcase.Run,
            ["simulated-time"] = SimulatedTimeProviderShowcase.Run,
            ["hlc-messaging"] = HlcMessagingShowcase.Run,
            ["hlc-coordinator"] = HlcCoordinatorShowcase.Run,
        };
}
