using System;
using System.Drawing;

namespace MobiusEditor.Utility
{
    /// <summary>
    /// The .NET Framework seeded Random (Knuth subtractive generator), so map content derived
    /// from a seed is identical on every runtime and matches maps the original editor produced.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const int MBIG = int.MaxValue;
        private const int MSEED = 161803398;
        private readonly int[] seedArray = new int[56];
        private int inext;
        private int inextp;

        public DeterministicRandom(int seed)
        {
            int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
            int mj = MSEED - subtraction;
            seedArray[55] = mj;
            int mk = 1;
            for (int i = 1; i < 55; i++)
            {
                int ii = (21 * i) % 55;
                seedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0) mk += MBIG;
                mj = seedArray[ii];
            }
            for (int k = 1; k < 5; k++)
            {
                for (int i = 1; i < 56; i++)
                {
                    seedArray[i] -= seedArray[1 + (i + 30) % 55];
                    if (seedArray[i] < 0) seedArray[i] += MBIG;
                }
            }
            inext = 0;
            inextp = 21;
        }

        /// <summary>
        /// Per-cell seed: corefx's pre-HashCode Point.GetHashCode (HashHelpers.Combine), which is
        /// what the mono-hosted editor computes. Modern .NET randomises Point.GetHashCode per process.
        /// </summary>
        public static int CellSeed(Point location) => unchecked(((location.X << 5) + location.X) ^ location.Y);

        private int InternalSample()
        {
            int locINext = inext;
            int locINextp = inextp;
            if (++locINext >= 56) locINext = 1;
            if (++locINextp >= 56) locINextp = 1;
            int retVal = seedArray[locINext] - seedArray[locINextp];
            if (retVal == MBIG) retVal--;
            if (retVal < 0) retVal += MBIG;
            seedArray[locINext] = retVal;
            inext = locINext;
            inextp = locINextp;
            return retVal;
        }

        private double Sample() => InternalSample() * (1.0 / MBIG);

        public int Next() => InternalSample();

        public int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            return (int)(Sample() * maxValue);
        }
    }
}
