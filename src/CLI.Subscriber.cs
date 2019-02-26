using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    public partial class CLI
    {
        [Command(Name = "subscriber")]
        public class SubscriberCLI
        {
            private int OnExecute(CommandLineApplication app, IConsole console)
            {
                console.WriteLine("You must specify  a subcommand.");
                console.WriteLine();
                app.ShowHelp();
                return 1;
            }
        }
    }
}
