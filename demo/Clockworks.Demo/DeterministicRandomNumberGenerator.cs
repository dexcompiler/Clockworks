using System.Security.Cryptography;

namespace Clockworks.Demo;

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
internal sealed class DeterministicRandomNumberGenerator : RandomNumberGenerator
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

    /// <summary>
    /// Create a new generator with a derived seed for parallel test isolation.
    /// </summary>
    public DeterministicRandomNumberGenerator Derive(int index)
    {
        return new DeterministicRandomNumberGenerator(HashCode.Combine(_seed, index));
    }
}