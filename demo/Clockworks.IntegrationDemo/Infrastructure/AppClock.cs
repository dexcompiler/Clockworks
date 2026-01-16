using Clockworks;

namespace Clockworks.IntegrationDemo.Infrastructure;

public static class AppClock
{
    public const string HeaderName = Clockworks.Distributed.HlcMessageHeader.HeaderName;

    // For demo, time comes from TimeProvider. In simulation we replace with SimulatedTimeProvider.
    public static TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
