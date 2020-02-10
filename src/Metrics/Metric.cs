// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Filtering;
using App.Metrics.Formatters.InfluxDB;
using App.Metrics.Reporting.Socket.Client;

namespace EGBench
{
    public static class Metric
    {
        private const string Context = "EGBench";
        private static readonly object LockObj = new object();
        private static ICounter eventsPublished;
        private static ICounter eventsReceived;
        private static IMetrics metrics = new NoOpMetrics();
        private static string runTag;

        public static ICounter EventsPublished
        {
            get
            {
                if (eventsPublished == null)
                {
                    lock (LockObj)
                    {
                        if (eventsPublished == null)
                        {
                            eventsPublished = CreateCounter(metrics, nameof(EventsPublished), ("Run-Tag", runTag));
                        }
                    }
                }

                return eventsPublished;
            }
        }

        public static ICounter EventsReceived
        {
            get
            {
                if (eventsReceived == null)
                {
                    lock (LockObj)
                    {
                        if (eventsReceived == null)
                        {
                            eventsReceived = CreateCounter(metrics, nameof(EventsReceived), ("Run-Tag", runTag));
                        }
                    }
                }

                return eventsReceived;
            }
        }

        public static void Initialize(CLI root)
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
                        options.Filter = new MetricsFilter().WhereContext(Context);
                        options.SocketSettings.Address = telegrafConfig.Value.telegrafAddress;
                        options.SocketSettings.Port = telegrafConfig.Value.telegrafPort.Value;
                        options.MetricsOutputFormatter = outputFormatter;
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

            metrics = metricsRoot;
            runTag = root.RunTag;

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

        private static ICounter CreateCounter(IMetrics metrics, string counterName, params (string key, string value)[] tagPairs)
        {
            var tags = new MetricTags(tagPairs.Select(tp => tp.key).ToArray(), tagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

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

        private static string ValueOrDefault(string input) => input == null ? "<NULL>" : input.Length == 0 ? "<EMPTY>" : input;
    }
}
