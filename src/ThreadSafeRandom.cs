// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace EGBench
{
    public static class ThreadSafeRandom
    {
        private const long SeedFactor = 45930423923L;
        private static readonly ThreadLocal<Random> Rng = new ThreadLocal<Random>(() => new Random(GetUniqueSeed()));
        private static long seed = 15263374115782L;

        public static int Next(int minValue, int maxValue)
        {
            return Rng.Value.Next(minValue, maxValue);
        }

        private static int GetUniqueSeed()
        {
            long next, current;
            do
            {
                current = Interlocked.Read(ref seed);
                next = current * SeedFactor;
            }
            while (Interlocked.CompareExchange(ref seed, next, current) != current);

            return (int)next ^ Environment.TickCount;
        }
    }
}
