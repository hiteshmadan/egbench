// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace EGBench
{
    // This is meant to replace DateTime when it is primarily used to determine elapsed time. DateTime is vulnerable to clock jump when
    // system wall clock is reset. Stopwatch can be used in similar scenario but it is not optimized for memory foot-print.
    //
    // This class is immune to clock jump with the following two exceptions:
    //  - When multi-processor machine has a bug in BIOS/HAL that returns inconsistent clock tick for different processor.
    //  - When the machine does not support high frequency CPU tick.
    public struct Timestamp : IEquatable<Timestamp>
    {
        public static readonly float TimestampToTicks = TimeSpan.TicksPerSecond / (float)Stopwatch.Frequency;
        public static readonly float TicksToTimestamp = Stopwatch.Frequency / (float)TimeSpan.TicksPerSecond;

        [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "false alarm, this is a struct and making this field readonly will result in every read of the struct resetting the value to 0")]
        private long timestamp;

        private Timestamp(long timestamp)
        {
            this.timestamp = timestamp;
        }

        public static Timestamp Now => new Timestamp(Stopwatch.GetTimestamp());

        public TimeSpan Elapsed => new TimeSpan(this.GetElapsedTicks());

        public long ElapsedMilliseconds => this.GetElapsedTicks() / TimeSpan.TicksPerMillisecond;

        public long ElapsedSeconds => this.GetElapsedTicks() / TimeSpan.TicksPerSecond;

        public long ElapsedMinutes => this.GetElapsedTicks() / TimeSpan.TicksPerMinute;

        public static bool operator ==(Timestamp t1, Timestamp t2) => t1.timestamp == t2.timestamp;

        public static bool operator !=(Timestamp t1, Timestamp t2) => t1.timestamp != t2.timestamp;

        public static bool operator >(Timestamp t1, Timestamp t2) => t1.timestamp > t2.timestamp;

        public static bool operator >=(Timestamp t1, Timestamp t2) => t1.timestamp >= t2.timestamp;

        public static bool operator <(Timestamp t1, Timestamp t2) => t1.timestamp < t2.timestamp;

        public static bool operator <=(Timestamp t1, Timestamp t2) => t1.timestamp <= t2.timestamp;

        public static Timestamp operator +(Timestamp t, TimeSpan duration)
        {
            long timestamp = (long)(t.timestamp + (duration.Ticks * TicksToTimestamp));
            return new Timestamp(timestamp);
        }

        public static Timestamp operator -(Timestamp t, TimeSpan duration)
        {
            long timestamp = (long)(t.timestamp - (duration.Ticks * TicksToTimestamp));
            return new Timestamp(timestamp);
        }

        public static TimeSpan operator -(Timestamp t1, Timestamp t2) => t1.Subtract(t2);

        public bool Equals(Timestamp other) => this.timestamp == other.timestamp;

        public override int GetHashCode() => this.timestamp.GetHashCode();

        public override bool Equals(object obj) => (obj is Timestamp ts) ? this.Equals(ts) : false;

        public TimeSpan Subtract(Timestamp other)
        {
            long elapsedTimestamp = this.timestamp - other.timestamp;
            long elapsedTicks = (long)(TimestampToTicks * elapsedTimestamp);
            return new TimeSpan(elapsedTicks);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetElapsedTicks()
        {
            // Start timestamp can't be zero in an initialized Timestamp. It would have to be literally the first thing executed when the machine boots to be 0.
            // So it being 0 is a clear indication of an untracked timestamp.
            if (this.timestamp == 0)
            {
                return 0;
            }

            long elapsedTimestamp = Stopwatch.GetTimestamp() - this.timestamp;
            if (elapsedTimestamp < 0)
            {
                return 0;
            }

            return (long)(TimestampToTicks * elapsedTimestamp);
        }
    }
}
