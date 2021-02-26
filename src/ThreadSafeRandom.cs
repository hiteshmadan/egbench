// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace EGBench
{
    public static class ThreadSafeRandom
    {
        private static readonly ThreadLocal<Random> Rng = new ThreadLocal<Random>(() => new Random((int)((Stopwatch.GetTimestamp() % int.MaxValue) - 1)));

        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not using this rng for any security/cryptography.")]
        public static int Next(int minValue, int maxValue) => Rng.Value.Next(minValue, maxValue);
    }
}
