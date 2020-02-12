// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Filtering;
using App.Metrics.Formatters.InfluxDB;
using App.Metrics.Histogram;
using App.Metrics.Reporting.Socket.Client;
using App.Metrics.ReservoirSampling;

namespace EGBench
{
    public static class Metric
    {
        private const string Context = "EGBench";
        private const string RunTagKey = "RunTag";
        private const string Status = "Status";
        private const string Success = "Success";
        private const string Error = "Error";

        private static readonly Unit UnitMs = Unit.Custom("ms");
        private static IMetrics metrics;
        private static string runTag;

        public static ICounter SuccessEventsPublished { get; private set; }

        public static ICounter SuccessRequestsPublished { get; private set; }

        public static ICounter ErrorRequestsPublished { get; private set; }

        public static IHistogram SuccessPublishLatencyMs { get; private set; }

        public static IHistogram ErrorPublishLatencyMs { get; private set; }

        public static ICounter SuccessEventsDelivered { get; private set; }

        public static ICounter SuccessRequestsDelivered { get; private set; }

        public static ICounter ErrorRequestsDelivered { get; private set; }

        public static IHistogram SuccessDeliveryLatencyMs { get; private set; }

        public static IHistogram ErrorDeliveryLatencyMs { get; private set; }

        public static void InitializePublisher(CLI root)
        {
            if (metrics != null)
            {
                return;
            }

            (metrics, runTag) = Helper.Initialize(root);
            SuccessEventsPublished = Helper.CreateCounter(metrics, runTag, "Events Published");
            SuccessRequestsPublished = Helper.CreateCounter(metrics, runTag, "Requests Published", (Status, Success));
            SuccessPublishLatencyMs = Helper.CreateHistogram(metrics, runTag, "Request Publish Latency ms", (Status, Success));

            ErrorRequestsPublished = Helper.CreateCounter(metrics, runTag, "Requests Published", (Status, Error));
            ErrorPublishLatencyMs = Helper.CreateHistogram(metrics, runTag, "Request Publish Latency ms", (Status, Error));
        }

        public static void InitializeSubscriber(CLI root)
        {
            if (metrics != null)
            {
                return;
            }

            (metrics, runTag) = Helper.Initialize(root);
            SuccessEventsDelivered = Helper.CreateCounter(metrics, runTag, "Events Delivered");
            SuccessRequestsDelivered = Helper.CreateCounter(metrics, runTag, "Requests Delivered", (Status, Success));
            SuccessDeliveryLatencyMs = Helper.CreateHistogram(metrics, runTag, "Request Delivery Latency ms", (Status, Success));

            ErrorRequestsDelivered = Helper.CreateCounter(metrics, runTag, "Requests Delivered", (Status, Error));
            ErrorDeliveryLatencyMs = Helper.CreateHistogram(metrics, runTag, "Request Delivery Latency ms", (Status, Error));
        }

        private static class Helper
        {
            public static (IMetricsRoot m, string r) Initialize(CLI root)
            {
                if (root == null)
                {
                    throw new NullReferenceException("root should not be null.");
                }

                (string telegrafAddress, int? telegrafPort)? telegrafConfig = null;
                if (root.TelegrafAddress.HasValue && root.TelegrafPort.HasValue)
                {
                    telegrafConfig = (root.TelegrafAddress.Value, root.TelegrafPort.Value);
                }

                string reporterType = telegrafConfig.HasValue ? "TELEGRAF" : "CONSOLE";

                var builder = new MetricsBuilder();
                var outputFormatter = new MetricsInfluxDbLineProtocolOutputFormatter();

                switch (reporterType.ToUpperInvariant())
                {
                    case "TELEGRAF":
                        if (string.IsNullOrEmpty(telegrafConfig.Value.telegrafAddress))
                        {
                            throw new InvalidOperationException("telegrafAddress");
                        }

                        if (telegrafConfig.Value.telegrafPort == null)
                        {
                            throw new InvalidOperationException("telegrafPort");
                        }

                        builder.Report.OverUdp(options =>
                        {
                            options.MetricsOutputFormatter = outputFormatter;
                            options.Filter = new MetricsFilter().WhereContext(Context);
                            options.SocketSettings.Address = telegrafConfig.Value.telegrafAddress;
                            options.SocketSettings.Port = telegrafConfig.Value.telegrafPort.Value;
                            options.SocketPolicy = new SocketPolicy
                            {
                                FailuresBeforeBackoff = 3,
                                Timeout = TimeSpan.FromSeconds(10),
                                BackoffPeriod = TimeSpan.FromSeconds(20),
                            };
                        });
                        break;

                    case "CONSOLE":
                    default:
                        builder.Report.ToConsole(options =>
                        {
                            options.MetricsOutputFormatter = outputFormatter;
                            options.Filter = new MetricsFilter().WhereContext(Context);
                        });
                        break;
                }

                IMetricsRoot metricsRoot = builder.Build();
                _ = Task.Run(() => ReportingLoop(metricsRoot, root.MetricsIntervalSeconds));
                return (metricsRoot, root.RunTag);

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

            public static ICounter CreateCounter(IMetrics metrics, string runTag, string counterName, params (string key, string value)[] tagPairs)
            {
                (string key, string value)[] mergedTagPairs =
                    new[] { (RunTagKey, runTag) }
                    .Concat(tagPairs)
                    .ToArray();

                var tags = new MetricTags(mergedTagPairs.Select(tp => tp.key).ToArray(), mergedTagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

                return metrics.Provider.Counter.Instance(new CounterOptions
                {
                    Name = counterName,
                    Context = Context,
                    MeasurementUnit = Unit.Requests,
                    ReportItemPercentages = false,
                    ReportSetItems = false,
                    ResetOnReporting = true,
                    Tags = tags,
                });
            }

            public static IHistogram CreateHistogram(IMetrics metrics, string runTag, string counterName, params (string key, string value)[] tagPairs)
            {
                (string key, string value)[] mergedTagPairs =
                    new[] { (RunTagKey, runTag) }
                    .Concat(tagPairs)
                    .ToArray();

                var tags = new MetricTags(mergedTagPairs.Select(tp => tp.key).ToArray(), mergedTagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

                return metrics.Provider.Histogram.Instance(new HistogramOptions
                {
                    Name = counterName,
                    Context = Context,
                    MeasurementUnit = UnitMs,
                    Reservoir = CreateReservoir,
                    Tags = tags
                });
            }

            private static string ValueOrDefault(string input) => input == null ? "<NULL>" : input.Length == 0 ? "<EMPTY>" : input;

            private static IReservoir CreateReservoir() => new CustomReservoir();
        }

        /// <summary>
        /// The sliding window reservoir has two problems:
        /// 1. a fixed sample size, which impacts reporting CPU usage (because that array of samples is copied/sorted/etc. whenever its reported)
        /// 2. aggregates are calculated only on the most recent N samples (where N=sampleSize), and thus can miss a majority of the data from the time the last report went out.
        /// If we're doing 10k reports a second, reported once a minute, we'll need a sample size of 600k entries which'll be too expensive to sort/copy (in addition to a LOH hit)
        /// If we use a more reasonable sample size - say 10k entries, we're effectively only reporting 1 second of data and missing 60 seconds.
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
