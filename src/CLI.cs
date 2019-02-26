using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{

    [Command(Name = "dotnet egbench.dll")]
    [Subcommand(typeof(PublisherCLI), typeof(SubscriberCLI))]
    [HelpOption]
    public partial class CLI
    {
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
