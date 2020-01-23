// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EGBench
{
    public partial class CLI
    {
        [Command(Name = "subscriber")]
        [Subcommand(typeof(StartListenerCommand))]
        public class SubscriberCLI
        {
            public CLI Parent { get; set; }

            private int OnExecute(CommandLineApplication app, IConsole console)
            {
                EGBenchLogger.WriteLine(console, "You must specify a subcommand.");
                console.WriteLine();
                app.ShowHelp();
                return 1;
            }

            [Command(Name = "start")]
            public class StartListenerCommand
            {
                private IWebHost host;

                public SubscriberCLI Parent { get; }

                public CLI Root => this.Parent.Parent;

                [Option("", "", CommandOptionType.SingleValue)]
                [Required]
                public ushort Port { get; set; }

                [Option("", "", CommandOptionType.SingleValue)]
                public uint? DelayInMs { get; set; }

                [Option("-t|--runtime-in-minutes", "Time after which the subscriber auto-shuts down.", CommandOptionType.SingleValue)]
                [Required]
                public ushort RuntimeInMinutes { get; set; }

                public async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
                {
                    Metric.Initialize(this.Root);

                    this.host = WebHost.
                        CreateDefaultBuilder<ListenerStartup>(Array.Empty<string>())
                        .UseKestrel()
                        .UseUrls($"http://127.0.0.1:{this.Port}")
                        .ConfigureServices(services =>
                        {
                            services.AddSingleton<StartListenerCommand>(this);
                        })
                        .ConfigureLogging(lb =>
                        {
                            lb.SetMinimumLevel(LogLevel.Warning);
                        })
                        .Build();

                    using (var stopHostCts = new CancellationTokenSource())
                    {
                        await this.host.StartAsync(stopHostCts.Token);
                        TaskCompletionSource<int> tcs = CreateTcs(stopHostCts, this.host, console, this.RuntimeInMinutes);

                        string endpointUrl = this.host.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First(s => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
                        console.WriteLine($"Started webserver at {endpointUrl}");
                        int result = await tcs.Task;
                        return result;
                    }
                }

                private static TaskCompletionSource<int> CreateTcs(CancellationTokenSource stopHostCts, IWebHost host, IConsole console, int runtimeInMinutes)
                {
                    var tcs = new TaskCompletionSource<int>();

                    // on runtime expiry, signal stopHostCts and tcs
                    _ = Task.Delay(TimeSpan.FromMinutes(runtimeInMinutes)).ContinueWith(
                        t =>
                        {
                            console.WriteLine($"--runtime-in-minutes ({runtimeInMinutes}) Minutes have passed, shutting down publishers.");
                            try
                            {
                                stopHostCts.Cancel();
                                tcs.TrySetResult(0);
                            }
                            catch (Exception ex)
                            {
                                tcs.TrySetException(ex);
                            }
                        },
                        TaskScheduler.Default);

                    // on host crash, signal tcs
                    _ = host.WaitForShutdownAsync().ContinueWith(
                        t =>
                        {
                            if (t.IsFaulted)
                            {
                                tcs.TrySetException(t.Exception);
                            }
                            else
                            {
                                tcs.TrySetResult(1);
                            }
                        },
                        TaskScheduler.Default);

                    return tcs;
                }
            }
        }
    }
}
