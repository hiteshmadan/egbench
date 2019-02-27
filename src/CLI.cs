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

        public static void Main(string[] args) => CommandLineApplication.Execute<CLI>(args);

        private int OnExecute(CommandLineApplication app, IConsole console)
        {
            console.WriteLine("You must specify  a subcommand.");
            console.WriteLine();
            app.ShowHelp();
            return 1;
        }
    }
}
