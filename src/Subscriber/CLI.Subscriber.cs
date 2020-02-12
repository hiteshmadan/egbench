// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EGBench
{
    public partial class CLI
    {
        [Command(Name = "subscriber", Description = "HTTP endpoint to act as a webhook sink for eventgrid deliveries.")]
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

                [Option("-p|--port", "REQUIRED. Port on which to listen", CommandOptionType.SingleValue)]
                [Required]
                public ushort Port { get; set; }

                [Option("-t|--runtime-in-minutes", "Time after which the subscriber auto-shuts down, defaults to 120 minutes. Set to 0 to never autoshutdown.", CommandOptionType.SingleValue)]
                public ushort RuntimeInMinutes { get; set; } = 120;

                [Option("-m|--meanDelayMs", "Fixed subscriber delay (in milliseconds). If stdDevDelayInMs is specified, delays are generated via a normal/gaussian distribution, Specify the mean of the distribution here.", CommandOptionType.SingleValue)]
                public uint MeanDelayInMs { get; set; } = 0;

                [Option("-s|--stdDevDelayMs", "If non-zero, Subscriber delays (in milliseconds) get generated via a normal/gaussian distribution. Specify the standard deviation of the distribution here.", CommandOptionType.SingleValue)]
                public uint StdDevDelayInMs { get; set; } = 0;

                [Option("-r|--return-code", "HTTP Status code to be returned, formatted as (%,HttpCode). ... -r \"10,400\" -r \"90:200\" would result in 10% HTTP 400 responses and 90% HTTP 200. All entries must sum to 100%. Valid separators: , : ; | _ <space>", CommandOptionType.MultipleValue)]
                public string[] ReturnStatusCodes { get; set; } = new[] { "100,200" };

                internal (int percent, HttpStatusCode result)[] StatusCodeMap { get; set; }

                public async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
                {
                    PropertyInfo[] options = this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<OptionAttribute>() != null).ToArray();
                    EGBenchLogger.WriteLine(console, $"Subscriber arguments (merged from cmdline and code defaults):\n{string.Join("\n", options.Select(o => $"{o.Name}={o.GetValue(this)}"))}");

                    this.StatusCodeMap = this.ReturnStatusCodes
                        .Select((s, i) =>
                        {
                            string s2 = s.Trim();
                            string[] ss = s2.Split(',', ';', ' ', '|', '_', ':');
                            string error = string.Empty;
                            if (ss.Length != 2)
                            {
                                error = $"return-code at index {i} with string value of '{s}' is invalid. Valid examples: -r \"10,400\" -r \"90:200\"";
                            }
                            else if (!int.TryParse(ss[0].Trim(), out int percent))
                            {
                                error = $"return-code at index {i} with string value of '{s}' has a percentage value that can't be parsed to an integer.";
                            }
                            else if (!Enum.TryParse<HttpStatusCode>(ss[1].Trim(), out HttpStatusCode result))
                            {
                                error = $"return-code at index {i} with string value of '{s}' has a http status code value that can't be parsed to the HttpStatusCode type.";
                            }
                            else
                            {
                                return (percent, result);
                            }

                            throw new InvalidOperationException(error);
                        })
                        .ToArray();

                    if (this.StatusCodeMap.Sum(kvp => kvp.percent) != 100)
                    {
                        throw new InvalidOperationException($"Sum of all percentages should be 100, was found to be {this.StatusCodeMap.Sum(kvp => kvp.percent)}. Parsed percentages={string.Join(',', this.StatusCodeMap.Select(kvp => kvp.percent))}");
                    }

                    Metric.InitializeSubscriber(this.Root);

                    this.host = new WebHostBuilder()
                        .UseContentRoot(Directory.GetCurrentDirectory())
                        .UseKestrel(options =>
                        {
                            options.Limits.MinRequestBodyDataRate = null;
                            options.Limits.MinResponseDataRate = null;
                        })
                        .UseUrls($"http://*:{this.Port}")
                        .ConfigureServices(services =>
                        {
                            services.AddSingleton<StartListenerCommand>(this);
                        })
                        .ConfigureLogging(lb =>
                        {
                            lb.SetMinimumLevel(LogLevel.Warning);
                        })
                        .UseStartup<ListenerStartup>()
                        .Build();

                    using (var stopHostCts = new CancellationTokenSource())
                    {
                        await this.host.StartAsync(stopHostCts.Token);
                        TaskCompletionSource<int> tcs = CreateTcs(stopHostCts, this.host, console, this.RuntimeInMinutes);
                        string endpointUrl = this.host.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First(s => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
                        EGBenchLogger.WriteLine(console, $"Started webserver at {endpointUrl}");
                        int result = await tcs.Task;
                        return result;
                    }
                }

                private static TaskCompletionSource<int> CreateTcs(CancellationTokenSource stopHostCts, IWebHost host, IConsole console, int runtimeInMinutes)
                {
                    var tcs = new TaskCompletionSource<int>();

                    if (runtimeInMinutes > 0)
                    {
                        // on runtime expiry, signal stopHostCts and tcs
                        _ = Task.Delay(TimeSpan.FromMinutes(runtimeInMinutes)).ContinueWith(
                            t =>
                            {
                                EGBenchLogger.WriteLine(console, $"--runtime-in-minutes ({runtimeInMinutes}) Minutes have passed, shutting down publishers.");
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
                    }

                    // on host shutdown, signal tcs
                    _ = host.WaitForShutdownAsync().ContinueWith(
                        t =>
                        {
                            if (t.IsFaulted)
                            {
                                tcs.TrySetException(t.Exception);
                            }
                            else
                            {
                                tcs.TrySetResult(0);
                            }
                        },
                        TaskScheduler.Default);

                    return tcs;
                }
            }
        }
    }
}
