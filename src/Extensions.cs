using Clockworks.Abstractions;
using Clockworks.Distributed;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clockworks;

/// <summary>
/// Dependency injection extensions for Clockworks.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the lock-free GUID factory with system time.
        /// Use this for most production scenarios.
        /// </summary>
        public IServiceCollection AddLockFreeGuidFactory(
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IUuidV7Factory>(sp => new UuidV7Factory(
                sp.GetRequiredService<TimeProvider>(),
                overflowBehavior: overflowBehavior));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the lock-free GUID factory with a custom TimeProvider.
        /// Use this for testing or simulation.
        /// </summary>
        public IServiceCollection AddLockFreeGuidFactory(
            TimeProvider timeProvider,
            RandomNumberGenerator? rng = null,
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
        {
            services.AddSingleton(timeProvider);
            services.AddSingleton<IUuidV7Factory>(new UuidV7Factory(timeProvider, rng, overflowBehavior));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the HLC GUID factory with system time.
        /// Use this for distributed systems requiring causal ordering.
        /// </summary>
        public IServiceCollection AddHlcGuidFactory(
            ushort nodeId = 0,
            HlcOptions? options = null)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IHlcGuidFactory>(sp => new HlcGuidFactory(
                sp.GetRequiredService<TimeProvider>(),
                nodeId,
                options));
            services.AddSingleton<IUuidV7Factory>(sp => sp.GetRequiredService<IHlcGuidFactory>());
            services.AddSingleton(sp => (HlcGuidFactory)sp.GetRequiredService<IHlcGuidFactory>());

            return services;
        }

        /// <summary>
        /// Adds the HLC GUID factory with a custom TimeProvider.
        /// Use this for testing or simulation.
        /// </summary>
        public IServiceCollection AddHlcGuidFactory(
            TimeProvider timeProvider,
            ushort nodeId = 0,
            HlcOptions? options = null,
            RandomNumberGenerator? rng = null)
        {
            services.AddSingleton(timeProvider);
            services.AddSingleton<IHlcGuidFactory>(new HlcGuidFactory(timeProvider, nodeId, options, rng));
            services.AddSingleton<IUuidV7Factory>(sp => sp.GetRequiredService<IHlcGuidFactory>());
            services.AddSingleton(sp => (HlcGuidFactory)sp.GetRequiredService<IHlcGuidFactory>());

            return services;
        }
    }
}

/// <summary>
/// Extension methods for GUID/UUID operations.
/// </summary>
public static class GuidExtensions
{
    extension(Guid guid)
    {
        /// <summary>
        /// Extracts the Unix timestamp (milliseconds) from a UUIDv7.
        /// Returns null if not a valid UUIDv7.
        /// </summary>
        public long? GetTimestampMs()
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes, bigEndian: true, out _);

            // Check version (bits 48-51 should be 0111)
            if ((bytes[6] & 0xF0) != 0x70)
                return null;

            // Extract 48-bit timestamp
            long timestamp = ((long)bytes[0] << 40) |
                            ((long)bytes[1] << 32) |
                            ((long)bytes[2] << 24) |
                            ((long)bytes[3] << 16) |
                            ((long)bytes[4] << 8) |
                            bytes[5];

            return timestamp;
        }

        /// <summary>
        /// Extracts the timestamp as a DateTimeOffset from a UUIDv7.
        /// Returns null if not a valid UUIDv7.
        /// </summary>
        public DateTimeOffset? GetTimestamp()
        {
            var ms = guid.GetTimestampMs();
            return ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null;
        }

        /// <summary>
        /// Extracts the 12-bit counter value from a UUIDv7.
        /// Returns null if not a valid UUIDv7.
        /// </summary>
        public ushort? GetCounter()
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes, bigEndian: true, out _);

            if ((bytes[6] & 0xF0) != 0x70)
                return null;

            return (ushort)(((bytes[6] & 0x0F) << 8) | bytes[7]);
        }

        /// <summary>
        /// For HLC-encoded UUIDv7s, extracts the node ID.
        /// Returns null if not a valid UUIDv7 or node ID not encoded.
        /// </summary>
        public ushort? GetNodeId()
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes, bigEndian: true, out _);

            if ((bytes[6] & 0xF0) != 0x70)
                return null;

            // Node ID is in bytes 8-9 (after variant bits)
            return (ushort)(((bytes[8] & 0x3F) << 8) | bytes[9]);
        }

        /// <summary>
        /// Checks if this GUID is a valid UUIDv7.
        /// </summary>
        public bool IsVersion7()
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes, bigEndian: true, out _);

            // Check version (0111) and variant (10)
            return (bytes[6] & 0xF0) == 0x70 && (bytes[8] & 0xC0) == 0x80;
        }

        /// <summary>
        /// Compares two UUIDv7s by their timestamp and counter (temporal ordering).
        /// </summary>
        public int CompareByTimestamp(Guid right)
        {
            // UUIDv7 is designed to be lexicographically sortable
            // when using big-endian byte order
            Span<byte> leftBytes = stackalloc byte[16];
            Span<byte> rightBytes = stackalloc byte[16];

            guid.TryWriteBytes(leftBytes, bigEndian: true, out _);
            right.TryWriteBytes(rightBytes, bigEndian: true, out _);

            return leftBytes.SequenceCompareTo(rightBytes);
        }

        /// <summary>
        /// Reconstructs HLC timestamp from a UUIDv7 created by HlcGuidFactory.
        /// </summary>
        public HlcTimestamp? ToHlcTimestamp()
        {
            if (!guid.IsVersion7())
                return null;

            var timestampMs = guid.GetTimestampMs();
            var counter = guid.GetCounter();
            var nodeId = guid.GetNodeId();

            if (!timestampMs.HasValue || !counter.HasValue || !nodeId.HasValue)
                return null;

            return new HlcTimestamp(timestampMs.Value, counter.Value, nodeId.Value);
        }
    }
}

/// <summary>
/// Extension methods for TimeProvider.
/// </summary>
public static class TimeProviderExtensions
{
    extension(TimeProvider timeProvider)
    {
        /// <summary>
        /// Creates a UUIDv7 using this TimeProvider.
        /// Simple convenience method - for production use, prefer IGuidFactory.
        /// </summary>
        public Guid CreateVersion7()
            => Guid.CreateVersion7(timeProvider.GetUtcNow());

        /// <summary>
        /// Gets the current Unix timestamp in milliseconds.
        /// </summary>
        public long GetUnixTimeMilliseconds()
            => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }
}
