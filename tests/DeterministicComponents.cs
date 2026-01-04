using System.Security.Cryptography;

namespace Clockworks.Tests;

// <summary>
/// Deterministic pseudo-random number generator for test replay and simulation.
/// 
/// <para>
/// <b>WARNING:</b> This is NOT cryptographically secure!
/// Use ONLY for:
/// - Unit tests requiring reproducible UUIDs
/// - Simulation scenarios
/// - Debugging distributed systems
/// 
/// NEVER use in production for actual UUID generation.
/// </para>
/// 
/// <para>
/// <b>Mathematical Basis:</b>
/// Uses .NET's implementation of xoshiro256** algorithm internally.
/// Period: 2^256 - 1
/// Good statistical properties for testing, but predictable.
/// </para>
/// </summary>
public sealed class DeterministicRandomNumberGenerator : RandomNumberGenerator
{
    private readonly Random _random;
    private readonly int _seed;

    /// <summary>
    /// Creates a deterministic RNG with the specified seed.
    /// Same seed = same sequence of random bytes.
    /// </summary>
    public DeterministicRandomNumberGenerator(int seed)
    {
        _seed = seed;
        _random = new Random(seed);
    }

    /// <summary>
    /// The seed used to initialize this generator.
    /// </summary>
    public int Seed => _seed;

    public override void GetBytes(byte[] data) => _random.NextBytes(data);

    public override void GetBytes(Span<byte> data) => _random.NextBytes(data);

    public override void GetBytes(byte[] data, int offset, int count)
        => _random.NextBytes(data.AsSpan(offset, count));

    public override void GetNonZeroBytes(byte[] data)
    {
        _random.NextBytes(data);
        for (int i = 0; i < data.Length; i++)
        {
            while (data[i] == 0)
            {
                data[i] = (byte)_random.Next(1, 256);
            }
        }
    }

    public override void GetNonZeroBytes(Span<byte> data)
    {
        _random.NextBytes(data);
        for (int i = 0; i < data.Length; i++)
        {
            while (data[i] == 0)
            {
                data[i] = (byte)_random.Next(1, 256);
            }
        }
    }

    // <summary>
    /// Create a new generator with a derived seed for parallel test isolation.
    /// </summary>
    public DeterministicRandomNumberGenerator Derive(int index)
    {
        return new DeterministicRandomNumberGenerator(HashCode.Combine(_seed, index));
    }
}

/// <summary>
/// Factory for creating fully deterministic GUID generation setups.
/// Useful for test fixtures.
/// </summary>
public static class DeterministicGuidSetup
{
    public static (UuidV7Factory Factory, SimulatedTimeProvider Time, DeterministicRandomNumberGenerator Rng)
        CreateLockFree(int seed = 42, long startTimeUnixMs = 1704067200000)
    {
        var time = SimulatedTimeProvider.FromUnixMs(startTimeUnixMs);
        var rng = new DeterministicRandomNumberGenerator(seed);
        var factory = new UuidV7Factory(time, rng);
        return (factory, time, rng);
    }

    public static (HlcGuidFactory Factory, SimulatedTimeProvider Time, DeterministicRandomNumberGenerator Rng)
        CreateHlc(ushort nodeId = 0, int seed = 42, long startTimeUnixMs = 1704067200000)
    {
        var time = SimulatedTimeProvider.FromUnixMs(startTimeUnixMs);
        var rng = new DeterministicRandomNumberGenerator(seed);
        var factory = new HlcGuidFactory(time, nodeId, rng: rng);
        return (factory, time, rng);
    }

    public static (HlcGuidFactory[] Factories, SimulatedTimeProvider[] Times)
        CreateHlcCluster(int nodeCount, int baseSeed = 42, long startTimeUnixMs = 1704067200000)
    {
        var factories = new HlcGuidFactory[nodeCount];
        var times = new SimulatedTimeProvider[nodeCount];

        for (int i = 0; i < nodeCount; i++)
        {
            times[i] = SimulatedTimeProvider.FromUnixMs(startTimeUnixMs);
            var rng = new DeterministicRandomNumberGenerator(HashCode.Combine(baseSeed, i));
            factories[i] = new HlcGuidFactory(times[i], (ushort)i, rng: rng);
        }

        return (factories, times);
    }
}
