using Clockworks;

namespace Clockworks.IntegrationDemo.Infrastructure;

public static class AppClock
{
    public const string HeaderName = Clockworks.Distributed.HlcMessageHeader.HeaderName;

    private static TimeProvider _timeProvider = TimeProvider.System;

    public static TimeProvider TimeProvider
    {
        get => _timeProvider;
        set => _timeProvider = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static TimeProvider Configure(TimeMode mode)
    {
        TimeProvider = mode switch
        {
            TimeMode.System => TimeProvider.System,
            TimeMode.Simulated => SimulatedTimeProvider.FromEpoch(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        return TimeProvider;
    }
}
