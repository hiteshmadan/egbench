// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Filtering;
using App.Metrics.Formatters.Ascii;
using App.Metrics.Histogram;
using App.Metrics.ReservoirSampling;

namespace EGBench
{
    public static class Metric
    {
        private const string Status = nameof(Status);
        private const string Success = nameof(Success);
        private const string UserError = nameof(UserError);
        private const string SystemError = nameof(SystemError);

        private static readonly Unit UnitMs = Unit.Custom("ms");
        private static readonly Unit UnitCount = Unit.Custom("count");
        private static IMetricsRoot metricsRoot;

        public static ICounter PublishEventsSuccess { get; private set; }

        public static ICounter PublishRequestsSuccess { get; private set; }

        public static IHistogram PublishRequestLatencyMsSuccess { get; private set; }

        public static ICounter PublishEventsUserError { get; private set; }

        public static ICounter PublishRequestsUserError { get; private set; }

        public static IHistogram PublishRequestLatencyMsUserError { get; private set; }

        public static ICounter PublishEventsSystemError { get; private set; }

        public static ICounter PublishRequestsSystemError { get; private set; }

        public static IHistogram PublishRequestLatencyMsSystemError { get; private set; }

        public static ICounter PublishRequestsFailed { get; private set; }

        public static IHistogram SubscribeE2ELatencyMs { get; private set; }

        public static ICounter SubscribeEventsSuccess { get; private set; }

        public static ICounter SubscribeRequestsSuccess { get; private set; }

        public static IHistogram SubscribeRequestLatencyMsSuccess { get; private set; }

        public static ICounter SubscribeEventsUserError { get; private set; }

        public static ICounter SubscribeRequestsUserError { get; private set; }

        public static IHistogram SubscribeRequestLatencyMsUserError { get; private set; }

        public static ICounter SubscribeEventsSystemError { get; private set; }

        public static ICounter SubscribeRequestsSystemError { get; private set; }

        public static IHistogram SubscribeRequestLatencyMsSystemError { get; private set; }

        public static void InitializePublisher(CLI root)
        {
            if (metricsRoot != null)
            {
                return;
            }

            Initialize(root);
            PublishEventsSuccess = CreateCounter("Publish-Events", UnitCount, (Status, Success));
            PublishRequestsSuccess = CreateCounter("Publish-Requests", UnitCount, (Status, Success));
            PublishRequestLatencyMsSuccess = CreateHistogram("Publish-Request Latency (ms)", UnitMs, (Status, Success));

            PublishEventsUserError = CreateCounter("Publish-Events", UnitCount, (Status, UserError));
            PublishRequestsUserError = CreateCounter("Publish-Requests", UnitCount, (Status, UserError));
            PublishRequestLatencyMsUserError = CreateHistogram("Publish-Request Latency (ms)", UnitMs, (Status, UserError));

            PublishEventsSystemError = CreateCounter("Publish-Events", UnitCount, (Status, SystemError));
            PublishRequestsSystemError = CreateCounter("Publish-Requests", UnitCount, (Status, SystemError));
            PublishRequestLatencyMsSystemError = CreateHistogram("Publish-Request Latency (ms)", UnitMs, (Status, SystemError));

            PublishRequestsFailed = CreateCounter("Publish-Requests Failed", UnitCount);
        }

        public static void InitializeSubscriber(CLI root)
        {
            if (metricsRoot != null)
            {
                return;
            }

            Initialize(root);
            SubscribeE2ELatencyMs = CreateHistogram("Subscribe-E2E Latency (ms)", UnitMs);

            SubscribeEventsSuccess = CreateCounter("Subscribe-Events", UnitCount, (Status, Success));
            SubscribeRequestsSuccess = CreateCounter("Subscribe-Requests", UnitCount, (Status, Success));
            SubscribeRequestLatencyMsSuccess = CreateHistogram("Subscribe-Request Latency (ms)", UnitMs, (Status, Success));

            SubscribeEventsUserError = CreateCounter("Subscribe-Events", UnitCount, (Status, UserError));
            SubscribeRequestsUserError = CreateCounter("Subscribe-Requests", UnitCount, (Status, UserError));
            SubscribeRequestLatencyMsUserError = CreateHistogram("Subscribe-Request Latency (ms)", UnitMs, (Status, UserError));

            SubscribeEventsSystemError = CreateCounter("Subscribe-Events", UnitCount, (Status, SystemError));
            SubscribeRequestsSystemError = CreateCounter("Subscribe-Requests", UnitCount, (Status, SystemError));
            SubscribeRequestLatencyMsSystemError = CreateHistogram("Subscribe-Request Latency (ms)", UnitMs, (Status, SystemError));
        }

        private static void Initialize(CLI root)
        {
            if (root == null)
            {
                throw new NullReferenceException("root should not be null.");
            }

            string context = root.RunTag ?? nameof(EGBench);

            IMetricsBuilder builder = AppMetrics.CreateDefaultBuilder()
                // .Filter.With(new NonZeroMetricsFilter())
                .Filter.With(new MetricsFilter().WhereContext(context))
                .Configuration.Configure(options =>
                {
                    options.DefaultContextLabel = context;
                    options.GlobalTags.Clear();
                    options.Enabled = true;
                    options.ReportingEnabled = true;
                });

            if (root.AppInsightsKey.HasValue)
            {
                EGBenchLogger.WriteLine($"Reporting metrics to application insights with instrumentation key={root.AppInsightsKey.Value}");
                builder.Report.ToApplicationInsights(root.AppInsightsKey.Value);
            }
            else
            {
                EGBenchLogger.WriteLine("Reporting metrics to console since --app-insights-key was not specified.");
                builder.Report.ToConsole(options =>
                {
                    options.MetricsOutputFormatter = new MetricsTextOutputFormatter();
                });
            }

            metricsRoot = builder.Build();

            _ = Task.Run(() => ReportingLoop(metricsRoot, root.MetricsIntervalSeconds));

            static async Task ReportingLoop(IMetricsRoot @metrics, int metricsIntervalSeconds)
            {
                while (true)
                {
                    await Task.Delay(metricsIntervalSeconds * 1000);

                    try
                    {
                        await Task.WhenAll(@metrics.ReportRunner.RunAllAsync(CancellationToken.None));
                    }
                    catch (Exception)
                    {
                        // TODO: Log exception somewhere?
                    }
                }
            }
        }

        private static ICounter CreateCounter(string counterName, Unit unit, params (string key, string value)[] tagPairs)
        {
            var tags = new MetricTags(tagPairs.Select(tp => tp.key).ToArray(), tagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

            return metricsRoot.Provider.Counter.Instance(new CounterOptions
            {
                Name = $"({nameof(EGBench)}) {counterName}",
                MeasurementUnit = unit,
                ReportItemPercentages = false,
                ReportSetItems = false,
                ResetOnReporting = true,
                Tags = tags,
            });
        }

        private static IHistogram CreateHistogram(string counterName, Unit unit, params (string key, string value)[] tagPairs)
        {
            var tags = new MetricTags(tagPairs.Select(tp => tp.key).ToArray(), tagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

            return metricsRoot.Provider.Histogram.Instance(new HistogramOptions
            {
                Name = $"({nameof(EGBench)}) {counterName}",
                MeasurementUnit = unit,
                Reservoir = CreateReservoir,
                Tags = tags,
            });
        }

        private static string ValueOrDefault(string input) => input == null ? "<NULL>" : input.Length == 0 ? "<EMPTY>" : input;

        private static IReservoir CreateReservoir() => new CustomReservoir();

        /// <summary>
        /// The sliding window reservoir has two problems:
        /// 1. a fixed sample size, which impacts reporting CPU usage (because that array of samples is copied/sorted/etc. whenever its reported)
        /// 2. aggregates are calculated only on the most recent N samples (where N=sampleSize), and thus can miss a majority of the data from the time the last report went out.
        /// If we're doing 10k reports a second, reported once a minute, we'll need a sample size of 600k entries which'll be too expensive to sort/copy (in addition to a LOH hit)
        /// If we use a more reasonable sample size-say 10k entries, we're effectively only reporting 1 second of data and missing 60 seconds.
        /// Thus this custom reservoir exists to allow accurate min/max/mean aggregates (while giving up the stddev/median/percentile aggregates altogether).
        /// </summary>
        private class CustomReservoir : IReservoir
        {
            private long count;
            private double sum;
            private long max;
            private long min;

            public CustomReservoir()
            {
                this.Reset();
            }

            public IReservoirSnapshot GetSnapshot(bool resetReservoir) => this.GetSnapshot();

            public IReservoirSnapshot GetSnapshot()
            {
                if (this.count == default)
                {
                    return Snapshot.Empty;
                }

                var result = new Snapshot(this.count, this.sum, this.max, this.min);

                // always reset, as the default behavior of Reservoir reporting is to NOT reset.
                this.Reset();
                return result;
            }

            public void Reset()
            {
                this.count = default;
                this.sum = default;
                this.max = long.MinValue;
                this.min = long.MaxValue;
            }

            public void Update(long value, string userValue) => this.Update(value);

            public void Update(long value)
            {
                this.count++;
                this.sum += value;
                this.max = Math.Max(value, this.max);
                this.min = Math.Min(value, this.min);
            }

            private class Snapshot : IReservoirSnapshot
            {
                public static readonly Snapshot Empty = new Snapshot(0, 0, 0, 0);

                public Snapshot(long count, double sum, long max, long min)
                {
                    this.Count = count;
                    this.Sum = sum;
                    this.Max = max;
                    this.Min = min;
                    this.Mean = count > 0 ? (sum / count) : 0;
                }

                public long Count { get; }

                public double Sum { get; }

                public long Max { get; }

                public double Mean { get; }

                public long Min { get; }

                public string MaxUserValue => default;

                public double Median => default;

                public string MinUserValue => default;

                public double Percentile75 => default;

                public double Percentile95 => default;

                public double Percentile98 => default;

                public double Percentile99 => default;

                public double Percentile999 => default;

                public int Size => (int)Math.Min(int.MaxValue, this.Count);

                public double StdDev => default;

                public IEnumerable<long> Values => Array.Empty<long>();

                public double GetValue(double quantile) => 0.0;
            }
        }
    }
}
