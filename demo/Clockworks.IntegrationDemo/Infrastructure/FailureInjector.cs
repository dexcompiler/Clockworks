namespace Clockworks.IntegrationDemo.Infrastructure;

public sealed class FailureInjector
{
    private readonly Random _random = new(123);

    public double DropRate { get; set; }
    public double DuplicateRate { get; set; }
    public double ReorderRate { get; set; }
    public int MaxAdditionalDelayMs { get; set; }

    public FailureInjector()
    {
        DropRate = 0.05;
        DuplicateRate = 0.05;
        ReorderRate = 0.10;
        MaxAdditionalDelayMs = 250;
    }

    public bool ShouldDrop() => _random.NextDouble() < DropRate;
    public bool ShouldDuplicate() => _random.NextDouble() < DuplicateRate;
    public bool ShouldReorder() => _random.NextDouble() < ReorderRate;

    public int AdditionalDelayMs() => MaxAdditionalDelayMs <= 0 ? 0 : _random.Next(0, MaxAdditionalDelayMs + 1);
}
