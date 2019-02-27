using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Formatters.InfluxDB;
using App.Metrics.Reporting.Socket.Client;

namespace EGBench
{
    public static class Metric
    {
        public static ICounter EventsPublished { get; private set; }

        public static ICounter EventsReceived { get; private set; }

        // dummy implementation that negates the need for a thousand null checks all over the place.
        static Metric() => Initialize(new NoOpMetrics(), nameof(NoOpMetrics));

        public static void Initialize(CLI root)
        {
            (string telegrafAddress, int? telegrafPort)? telegrafConfig = null;
            if (root.TelegrafAddress.HasValue && root.TelegrafPort.HasValue)
            {
                telegrafConfig = (root.TelegrafAddress.Value, root.TelegrafPort.Value);
            }

            string runTag = root.RunTag;
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

                case "PROMETHEUS":
                    throw new NotImplementedException(reporterType);

                case "CONSOLE":
                default:
                    builder.Report.ToConsole(options =>
                    {
                        options.MetricsOutputFormatter = outputFormatter;
                    });
                    break;
            }

            IMetricsRoot metrics = builder.Build();
            Initialize(metrics, runTag);

            Task.Run(ReportingLoop);

            async Task ReportingLoop()
            {
                while (true)
                {
                    await Task.Delay(60 * 1000);

                    try
                    {
                        await Task.WhenAll(metrics.ReportRunner.RunAllAsync(CancellationToken.None));
                    }
                    catch (Exception)
                    {
                        // TODO: Log exception somewhere?
                    }
                }
            }
        }

        private static void Initialize(IMetrics metrics, string runTag)
        {
            EventsPublished = CreateCounter(metrics, nameof(EventsPublished), ("Run Tag", runTag));
            EventsReceived = CreateCounter(metrics, nameof(EventsReceived), ("Run Tag", runTag));
        }

        private static ICounter CreateCounter(IMetrics metrics, string counterName, params (string key, string value)[] tagPairs)
        {
            var tags = new MetricTags(tagPairs.Select(tp => tp.key).ToArray(), tagPairs.Select(tp => ValueOrDefault(tp.value)).ToArray());

            return metrics.Provider.Counter.Instance(new CounterOptions
            {
                Name = counterName,
                Context = "EGBench",
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
