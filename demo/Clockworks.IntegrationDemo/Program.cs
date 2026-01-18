using Clockworks.IntegrationDemo.Domain;
using Clockworks.IntegrationDemo.Infrastructure;
using Clockworks.IntegrationDemo.Simulation;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Text("Clockworks.IntegrationDemo. POST /simulate to run the workflow simulation."));

app.MapPost("/orders", (PlaceOrderRequest request) =>
{
    return Results.Ok(new
    {
        Received = request,
        Hint = "Use POST /simulate to run the full outbox/inbox + HLC demo"
    });
});

app.MapPost("/simulate", async (
    string? mode,
    int? orders,
    int? maxSteps,
    int? tickMs,
    bool? inMemorySqlite,
    string? sqliteFile,
    CancellationToken ct) =>
{
    var timeMode = Enum.TryParse<TimeMode>(mode ?? string.Empty, ignoreCase: true, out var parsed)
        ? parsed
        : TimeMode.Simulated;

    var options = new SimulationOptions(
        TimeMode: timeMode,
        Orders: Math.Clamp(orders ?? 3, 1, 100),
        MaxSteps: Math.Clamp(maxSteps ?? 5_000, 100, 200_000),
        TickMs: Math.Clamp(tickMs ?? 10, 1, 1_000),
        UseInMemorySqlite: inMemorySqlite ?? true,
        SqliteFile: sqliteFile);

    await SimulatedRunner.RunAsync(options, ct);

    return Results.Ok(new
    {
        Status = "Simulation finished. Check console output.",
        Options = options
    });
});

app.Run();
