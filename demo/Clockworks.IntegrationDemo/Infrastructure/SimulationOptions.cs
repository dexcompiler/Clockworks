namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed record SimulationOptions(
    TimeMode TimeMode,
    int Orders = 3,
    int MaxSteps = 5_000,
    int TickMs = 10,
    bool UseInMemorySqlite = true,
    string? SqliteFile = null);
