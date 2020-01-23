// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel.DataAnnotations;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    [Command(Name = "dotnet egbench.dll")]
    [Subcommand(typeof(PublisherCLI), typeof(SubscriberCLI))]
    [HelpOption]
    public partial class CLI
    {
        [Option(LongName = "runtag", Inherited = true)]
        [Required]
        public string RunTag { get; set; }

        [Option(LongName = "telegrafAddr", Inherited = true)]
        public (bool HasValue, string Value) TelegrafAddress { get; set; }

        [Option(LongName = "telegrafPort", Inherited = true)]
        public (bool HasValue, int Value) TelegrafPort { get; set; }

        public static void Main(string[] args)
        {
            EGBenchLogger.WriteLine($"Received args: {ArgumentEscaper.EscapeAndConcatenate(args)}");
            CommandLineApplication.Execute<CLI>(args);
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
