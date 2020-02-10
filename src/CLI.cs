// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    [Command(Name = "dotnet egbench.dll")]
    [Subcommand(typeof(PublisherCLI), typeof(SubscriberCLI))]
    [HelpOption]
    public partial class CLI
    {
        [Option("|--runtag", Description = "Used as a context for metrics reports. Defaults to DateTime.Now when the process starts.", Inherited = true)]
        public string RunTag { get; set; } = $"{DateTime.Now.ToString("yyyy-MM-dd_HH:mm:ss", CultureInfo.InvariantCulture)}";

        [Option("|--telegrafAddr", Inherited = true)]
        public (bool HasValue, string Value) TelegrafAddress { get; set; }

        [Option("|--telegrafPort", Inherited = true)]
        public (bool HasValue, int Value) TelegrafPort { get; set; }

        [Option("|--metrics-interval-seconds", Description = "Frequency of reporting metrics out to console/telegraf. Defaults to 60", Inherited = true)]
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
