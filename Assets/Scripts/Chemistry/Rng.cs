using System.Runtime.CompilerServices;

namespace Residue.Chemistry
{
    /// <summary>
    /// Deterministic xorshift128 PRNG.
    /// <para>
    /// Deliberately not <c>UnityEngine.Random</c> (global mutable state, not reproducible) and not
    /// <c>System.Random</c> (its algorithm differs between .NET Framework and .NET Core, so a seed
    /// does not pin a sequence). Sample generation is host-authoritative and unit-tested, so a seed
    /// must produce byte-identical results everywhere, forever.
    /// </para>
    /// Pass by <c>ref</c> — it is a mutable struct by design so no allocation occurs per sample.
    /// </summary>
    public struct Rng
    {
        private uint x, y, z, w;

        public Rng(int seed)
        {
            // SplitMix-style scramble so nearby seeds produce distant streams.
            unchecked
            {
                uint s = (uint)seed;
                x = Scramble(ref s);
                y = Scramble(ref s);
                z = Scramble(ref s);
                w = Scramble(ref s);
                if ((x | y | z | w) == 0u) x = 0x9E3779B9u; // xorshift cannot recover from all-zero state
            }
        }

        /// <summary>
        /// Copy the four words of live generator state out, so a saved run can be put back exactly
        /// where it stopped (#49).
        /// <para>
        /// <b>The seed is not enough.</b> A seed names the start of a stream and a saved run is
        /// halfway down it. Restoring from the seed would re-issue numbers the run had already
        /// spent, so the first sample generated after a load would not be the one an uninterrupted
        /// run would have produced — and hard rule 1 says a loaded contract must behave exactly like
        /// the one that was saved. That is why this is four words rather than one.
        /// </para>
        /// </summary>
        public void CaptureState(out uint a, out uint b, out uint c, out uint d)
        {
            a = x;
            b = y;
            c = z;
            d = w;
        }

        /// <summary>
        /// Rebuild a generator from <see cref="CaptureState"/>. An all-zero state is repaired the
        /// same way the seeded constructor repairs it: xorshift cannot recover from all zeroes, and
        /// a save that somehow carried one would otherwise return the same number forever.
        /// </summary>
        public static Rng FromState(uint a, uint b, uint c, uint d)
        {
            var restored = default(Rng);
            restored.x = a;
            restored.y = b;
            restored.z = c;
            restored.w = d;
            if ((a | b | c | d) == 0u) restored.x = 0x9E3779B9u;
            return restored;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Scramble(ref uint s)
        {
            unchecked
            {
                s += 0x9E3779B9u;
                uint v = s;
                v = (v ^ (v >> 16)) * 0x85EBCA6Bu;
                v = (v ^ (v >> 13)) * 0xC2B2AE35u;
                return v ^ (v >> 16);
            }
        }

        /// <summary>Uniform uint over the full range.</summary>
        public uint NextUInt()
        {
            unchecked
            {
                uint t = x ^ (x << 11);
                x = y; y = z; z = w;
                return w = w ^ (w >> 19) ^ t ^ (t >> 8);
            }
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + NextFloat() * (max - min);

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>
        /// Normally distributed value with the given mean and standard deviation (Box-Muller).
        /// Truncated to +/-3 sigma so a freak tail value cannot silently flip a verdict in a test.
        /// </summary>
        public float NextGaussian(float mean = 0f, float stdDev = 1f)
        {
            float u1 = 1f - NextFloat(); // in (0, 1]
            float u2 = NextFloat();
            float mag = stdDev * (float)System.Math.Sqrt(-2.0 * System.Math.Log(u1));
            float g = mag * (float)System.Math.Sin(2.0 * System.Math.PI * u2);
            float limit = 3f * stdDev;
            if (g > limit) g = limit;
            else if (g < -limit) g = -limit;
            return mean + g;
        }
    }
}
