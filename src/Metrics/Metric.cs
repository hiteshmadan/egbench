// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Filtering;
using App.Metrics.Formatters.InfluxDB;
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
        private const string ClientError = nameof(ClientError);

        private static readonly Unit UnitMs = Unit.Custom("ms");
        private static readonly Unit UnitCount = Unit.Custom("count");
        private static IMetricsRoot metricsRoot;
        private static bool isConsole;

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

            PublishRequestsFailed = CreateCounter("Publish-Requests", UnitCount, (Status, ClientError));
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
                throw new InvalidOperationException("root should not be null.");
            }

            string context = root.RunTag ?? nameof(EGBench);

            IMetricsBuilder builder = AppMetrics.CreateDefaultBuilder()
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
                isConsole = true;
                EGBenchLogger.WriteLine("Reporting metrics to console since --app-insights-key was not specified.");
                builder.Report.ToConsole(options =>
                {
                    options.MetricsOutputFormatter = new MetricsInfluxDbLineProtocolOutputFormatter();
                });
            }

            metricsRoot = builder.Build();

            _ = Task.Run(() => ReportingLoop(metricsRoot, root.MetricsIntervalSeconds));

            static async Task ReportingLoop(IMetricsRoot @metrics, int metricsIntervalSeconds)
            {
                Timestamp lastStartTime = Timestamp.Now;

                while (true)
                {
                    int waitInSeconds = Math.Clamp(metricsIntervalSeconds - (int)lastStartTime.ElapsedSeconds, 0, metricsIntervalSeconds);
                    if (waitInSeconds > 0)
                    {
                        await Task.Delay(waitInSeconds * 1000);
                    }

                    lastStartTime = Timestamp.Now;
                    try
                    {
                        await Task.WhenAll(@metrics.ReportRunner.RunAllAsync(CancellationToken.None));
                    }
                    catch (Exception ex)
                    {
                        EGBenchLogger.WriteLine($"Metrics reporting exception: {ex.Message}");
                    }
                }
            }
        }

        private static ICounter CreateCounter(string counterName, Unit unit, params (string key, string value)[] tagPairs)
        {
            var tags = new MetricTags(tagPairs.Select(tp => tp.key).ToArray(), tagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

            return metricsRoot.Provider.Counter.Instance(new CounterOptions
            {
                Name = $"({nameof(EGBench)})-{counterName}",
                MeasurementUnit = unit,
                ReportItemPercentages = false,
                ReportSetItems = false,
                Tags = tags,
                // If console, we don't have any failures or delays and thus want to see the "diff-ed" numbers instead of cumulative count.
                ResetOnReporting = isConsole
            });
        }

        private static IHistogram CreateHistogram(string counterName, Unit unit, params (string key, string value)[] tagPairs)
        {
            var tags = new MetricTags(tagPairs.Select(tp => tp.key).ToArray(), tagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

            return metricsRoot.Provider.Histogram.Instance(new HistogramOptions
            {
                Name = $"({nameof(EGBench)})-{counterName}",
                MeasurementUnit = unit,
                Reservoir = CreateReservoir,
                Tags = tags,
            });
        }

        private static string ValueOrDefault(string input) => input == null ? "<NULL>" : input.Length == 0 ? "<EMPTY>" : input;

        private static IReservoir CreateReservoir() => new LosslessReservoir();
    }
}
