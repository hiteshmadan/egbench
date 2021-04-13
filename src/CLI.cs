// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    [Command(Name = "dotnet egbench.dll")]
    [Subcommand(typeof(PublisherCLI), typeof(SubscriberCLI))]
    [HelpOption]
    public partial class CLI
    {
        [Option("|--runtag", Description = "Used as a context for metrics reports. Defaults to EGBench.", Inherited = true)]
        public string RunTag { get; set; }

        [Option("|--app-insights-key", Description = "Azure Application Insights key. If null, metrics are written to console in influxdb single lineformat.", Inherited = true)]
        public (bool HasValue, string Value) AppInsightsKey { get; set; }

        [Option("|--metrics-interval-seconds", Description = "Frequency of reporting metrics out to console/azmonitor. Defaults to 60", Inherited = true)]
        public int MetricsIntervalSeconds { get; set; } = 60;

        public static void Main(string[] args)
        {
            EGBenchLogger.WriteLine($"Received args: {ArgumentEscaper.EscapeAndConcatenate(args)}");

            try
            {
                int result = CommandLineApplication.Execute<CLI>(args);
                if (result == 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                EGBenchLogger.WriteLine($"Exception: {ex}");
            }
        }

        private int OnExecute(CommandLineApplication app, IConsole console)
        {
            EGBenchLogger.WriteLine(console, "You must specify a subcommand.");
            console.WriteLine();
            app.ShowHelp();
            return 1;
        }
    }
}
