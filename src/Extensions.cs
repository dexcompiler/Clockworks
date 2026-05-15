using Clockworks.Abstractions;
using Clockworks.Distributed;
using Clockworks.Instrumentation;
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
        /// Use this for most production scenarios. Registers a singleton factory so per-instance monotonic state is
        /// shared across callers in the process.
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
        /// Adds the lock-free GUID factory with system time and opt-in statistics.
        /// </summary>
        /// <param name="statistics">Statistics instance updated by the registered singleton factory.</param>
        /// <param name="overflowBehavior">Behavior to apply when the per-millisecond counter overflows.</param>
        public IServiceCollection AddLockFreeGuidFactory(
            UuidV7FactoryStatistics statistics,
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
        {
            ArgumentNullException.ThrowIfNull(statistics);

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(statistics);
            services.AddSingleton<IUuidV7Factory>(sp => new UuidV7Factory(
                sp.GetRequiredService<TimeProvider>(),
                rng: null,
                overflowBehavior: overflowBehavior,
                statistics: sp.GetRequiredService<UuidV7FactoryStatistics>()));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the lock-free GUID factory with a custom TimeProvider.
        /// Use this for testing or simulation. Registers a singleton factory so per-instance monotonic state is shared
        /// across callers in the process. The service provider disposes the created factory when the provider is
        /// disposed; externally supplied <paramref name="timeProvider"/> and <paramref name="rng"/> instances remain
        /// caller-owned.
        /// </summary>
        /// <param name="timeProvider">Time source used by the UUIDv7 factory.</param>
        /// <param name="rng">
        /// Random number generator used for the UUID random tail. Leave null for a per-factory CSPRNG. Seeded or
        /// deterministic RNGs are intended only for reproducible tests and simulations.
        /// </param>
        /// <param name="overflowBehavior">Behavior to apply when the per-millisecond counter overflows.</param>
        public IServiceCollection AddLockFreeGuidFactory(
            TimeProvider timeProvider,
            RandomNumberGenerator? rng = null,
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);

            services.TryAddSingleton(timeProvider);
            services.AddSingleton<IUuidV7Factory>(sp => new UuidV7Factory(
                sp.GetRequiredService<TimeProvider>(),
                rng,
                overflowBehavior));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the lock-free GUID factory with a custom TimeProvider and opt-in statistics.
        /// The service provider disposes the created factory when the provider is disposed; externally supplied
        /// <paramref name="timeProvider"/> and <paramref name="rng"/> instances remain caller-owned.
        /// </summary>
        /// <param name="timeProvider">Time source used by the UUIDv7 factory.</param>
        /// <param name="statistics">Statistics instance updated by the registered singleton factory.</param>
        /// <param name="rng">
        /// Random number generator used for the UUID random tail. Leave null for a per-factory CSPRNG. Seeded or
        /// deterministic RNGs are intended only for reproducible tests and simulations.
        /// </param>
        /// <param name="overflowBehavior">Behavior to apply when the per-millisecond counter overflows.</param>
        public IServiceCollection AddLockFreeGuidFactory(
            TimeProvider timeProvider,
            UuidV7FactoryStatistics statistics,
            RandomNumberGenerator? rng = null,
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(statistics);

            services.TryAddSingleton(timeProvider);
            services.AddSingleton(statistics);
            services.AddSingleton<IUuidV7Factory>(sp => new UuidV7Factory(
                sp.GetRequiredService<TimeProvider>(),
                rng,
                overflowBehavior,
                sp.GetRequiredService<UuidV7FactoryStatistics>()));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the lock-free GUID factory with system time and an opt-in UUIDv7 node partition.
        /// Use this for distributed fleets that can assign unique node, shard, process, or deployment IDs.
        /// </summary>
        /// <param name="nodePartition">Node partition to embed into the most-significant bits of UUIDv7 <c>rand_b</c>.</param>
        /// <param name="overflowBehavior">Behavior to apply when the per-millisecond counter overflows.</param>
        public IServiceCollection AddNodePartitionedGuidFactory(
            UuidV7NodePartition nodePartition,
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IUuidV7Factory>(sp => new UuidV7Factory(
                sp.GetRequiredService<TimeProvider>(),
                nodePartition,
                overflowBehavior: overflowBehavior));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the lock-free GUID factory with a custom TimeProvider and an opt-in UUIDv7 node partition.
        /// The service provider disposes the created factory when the provider is disposed; externally supplied
        /// <paramref name="timeProvider"/> and <paramref name="rng"/> instances remain caller-owned.
        /// </summary>
        /// <param name="timeProvider">Time source used by the UUIDv7 factory.</param>
        /// <param name="nodePartition">Node partition to embed into the most-significant bits of UUIDv7 <c>rand_b</c>.</param>
        /// <param name="rng">
        /// Random number generator used for the UUID random tail. Leave null for a per-factory CSPRNG. Seeded or
        /// deterministic RNGs are intended only for reproducible tests and simulations.
        /// </param>
        /// <param name="overflowBehavior">Behavior to apply when the per-millisecond counter overflows.</param>
        /// <param name="statistics">Optional statistics instance updated by the registered singleton factory.</param>
        public IServiceCollection AddNodePartitionedGuidFactory(
            TimeProvider timeProvider,
            UuidV7NodePartition nodePartition,
            RandomNumberGenerator? rng = null,
            CounterOverflowBehavior overflowBehavior = CounterOverflowBehavior.SpinWait,
            UuidV7FactoryStatistics? statistics = null)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);

            services.TryAddSingleton(timeProvider);
            if (statistics is not null)
                services.AddSingleton(statistics);

            services.AddSingleton<IUuidV7Factory>(sp => new UuidV7Factory(
                sp.GetRequiredService<TimeProvider>(),
                nodePartition,
                rng,
                overflowBehavior,
                statistics is null ? null : sp.GetRequiredService<UuidV7FactoryStatistics>()));
            services.AddSingleton(sp => (UuidV7Factory)sp.GetRequiredService<IUuidV7Factory>());

            return services;
        }

        /// <summary>
        /// Adds the HLC GUID factory with system time.
        /// Use this for distributed systems requiring causal ordering.
        /// </summary>
        /// <param name="nodeId">Unique 14-bit node identifier (0-16383) encoded into generated HLC UUIDv7 values.</param>
        /// <param name="options">HLC drift and overflow behavior options.</param>
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
        /// <param name="timeProvider">Time source used by the HLC factory.</param>
        /// <param name="nodeId">Unique 14-bit node identifier (0-16383) encoded into generated HLC UUIDv7 values.</param>
        /// <param name="options">HLC drift and overflow behavior options.</param>
        /// <param name="rng">Random number generator used for the UUID random tail.</param>
        public IServiceCollection AddHlcGuidFactory(
            TimeProvider timeProvider,
            ushort nodeId = 0,
            HlcOptions? options = null,
            RandomNumberGenerator? rng = null)
        {
            services.TryAddSingleton(timeProvider);
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
        /// For HLC-encoded UUIDv7s, extracts the 14-bit node ID.
        /// Returns null if not a valid UUIDv7 or node ID not encoded.
        /// </summary>
        public ushort? GetNodeId()
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes, bigEndian: true, out _);

            if ((bytes[6] & 0xF0) != 0x70)
                return null;

            // Node ID is in bytes 8-9 after masking off the two RFC variant bits.
            return (ushort)(((bytes[8] & 0x3F) << 8) | bytes[9]);
        }

        /// <summary>
        /// For node-partitioned UUIDv7s, extracts the configured node partition from the most-significant
        /// <c>rand_b</c> bits.
        /// </summary>
        /// <param name="nodeIdBitWidth">Number of <c>rand_b</c> bits reserved for the node partition.</param>
        /// <returns>
        /// The node partition ID, or <see langword="null"/> if this GUID is not UUIDv7 or the bit width is invalid.
        /// </returns>
        public ushort? GetNodePartitionId(byte nodeIdBitWidth)
        {
            if (nodeIdBitWidth is < UuidV7NodePartition.MinNodeIdBitWidth or > UuidV7NodePartition.MaxNodeIdBitWidth)
                return null;

            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes, bigEndian: true, out _);

            if ((bytes[6] & 0xF0) != 0x70 || (bytes[8] & 0xC0) != 0x80)
                return null;

            return UuidV7NodePartition.ReadNodeId(bytes, nodeIdBitWidth);
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
