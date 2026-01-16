using Clockworks.IntegrationDemo.Domain;
using Clockworks.IntegrationDemo.Simulation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

var app = builder.Build();

// app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapPost("/orders", (PlaceOrderRequest request) =>
{
    // This API stub exists to show where you'd accept requests.
    // The full workflow is demonstrated via /simulate to keep the demo deterministic and self-contained.
    return Results.Ok(new
    {
        Received = request,
        Hint = "Use POST /simulate to run the full outbox/inbox + HLC deterministic demo"
    });
});

app.MapPost("/simulate", async (CancellationToken ct) =>
{
    await SimulatedRunner.RunAsync(ct);
    return Results.Ok(new { Status = "Simulation finished. Check console output." });
});

app.Run();
