// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using App.Metrics.ReservoirSampling;

namespace EGBench
{
    /// <summary>
    /// The sliding window reservoir has the following problems:
    /// 1. high CPU hit to report aggregates: The array of samples is copied/sorted/etc. whenever its reported.
    /// 2. Aggregates are calculated only on the most recent N samples (where N=sampleSize), and thus can miss a majority of the data from the time the last report went out esp with high freq metrics.
    /// If we're doing 10k reports a second, reported once a minute, we'll need a sample size of 600k entries which'll be too expensive to sort/copy (in addition to being a LOH hit and too much memory usage.)
    /// If we use a more reasonable sample size-say 10k entries, we're effectively only reporting the most recent 1 second of data before every report, and missing 59 seconds.
    /// Thus this custom reservoir exists to allow accurate mean tracking (while giving up the min/max/stddev/median/percentile aggregates altogether).
    /// </summary>
    public class LosslessReservoir : IReservoir
    {
        private long count;
        private long sum;

        public LosslessReservoir()
        {
            this.Reset();
        }

        public IReservoirSnapshot GetSnapshot() => this.GetSnapshot(resetReservoir: true);

        public IReservoirSnapshot GetSnapshot(bool resetReservoir)
        {
            // always reset to zero, appmetrics default behavior is to never reset and rely on the reservoir to age out old values.
            // since we don't store the values, keeping the old values around will result in this reservoir representing a lifetime-average of the metric which is of no use in any scenario.
            return new Snapshot(Interlocked.Exchange(ref this.count, 0), Interlocked.Exchange(ref this.sum, 0));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref this.count, 0);
            Interlocked.Exchange(ref this.sum, 0);
        }

        public void Update(long value, string userValue) => this.Update(value);

        public void Update(long value)
        {
            Interlocked.Increment(ref this.count);
            Interlocked.Add(ref this.sum, value);
        }

        private class Snapshot : IReservoirSnapshot
        {
            public static readonly Snapshot Empty = new Snapshot(0, 0);
            private readonly long meanLong;

            public Snapshot(long count, long sum)
            {
                this.Count = count;
                this.Sum = sum;
                this.Mean = count > 0 ? (sum / (double)count) : 0;

                // report min/max/median/percentiles all as the same number.
                this.meanLong = Convert.ToInt64(this.Mean);
            }

            public long Count { get; }

            public double Sum { get; }

            public long Max => this.meanLong;

            public double Mean { get; }

            public long Min => this.meanLong;

            public string MaxUserValue => default;

            public double Median => this.Mean;

            public string MinUserValue => default;

            public double Percentile75 => this.Mean;

            public double Percentile95 => this.Mean;

            public double Percentile98 => this.Mean;

            public double Percentile99 => this.Mean;

            public double Percentile999 => this.Mean;

            public int Size => (int)Math.Min(int.MaxValue, this.Count);

            public double StdDev => default;

            public IEnumerable<long> Values => Array.Empty<long>();

            public double GetValue(double quantile) => this.Mean;
        }
    }
}
