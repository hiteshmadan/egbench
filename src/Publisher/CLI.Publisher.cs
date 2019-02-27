using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    public partial class CLI
    {
        [Command(Name = "publisher")]
        [Subcommand(typeof(StartPublishCommand))]
        public class PublisherCLI
        {
            public CLI Parent { get; set; }

            private int OnExecute(CommandLineApplication app, IConsole console)
            {
                console.WriteLine("You must specify  a subcommand.");
                console.WriteLine();
                app.ShowHelp();
                return 1;
            }

            [Command(Name = "start")]
            public class StartPublishCommand
            {
                public PublisherCLI Parent { get; }

                public CLI Root => this.Parent.Parent;

                [Option("-u|--topic-url", "URL to which events should be posted to", CommandOptionType.SingleValue)]
                [Required]
                public string Address { get; set; }

                [Option("-n|--topic-name", "String that should be used for stamping eventgrid event envelope's Topic field", CommandOptionType.SingleValue)]
                [Required]
                public string TopicName { get; set; }

                [Option("-p|--publishers", "Number of concurrent publishing calls made in every interval (the interval is controlled by rps-per-publisher)", CommandOptionType.SingleValue)]
                [Required]
                public short ConcurrentPublishersCount { get; set; }

                [Option("-r|--rps-per-publisher", "Inverse of the delay interval used to wake up each publisher and fire-and-forget a publish call.", CommandOptionType.SingleValue)]
                [Required]
                public short RequestsPerSecondPerPublisher { get; set; }

                [Option("-e|--events-per-request", "Batch size of each request's payload", CommandOptionType.SingleValue)]
                [Required]
                public ushort EventsPerRequest { get; set; }

                [Option("-s|--event-size-in-bytes", "Number of bytes per event. Total request payload size = 1 + (EventsPerRequest * (EventSizeInBytes + 1))", CommandOptionType.SingleValue)]
                [Required]
                public uint EventSizeInBytes { get; set; }

                [Option("-t|--runtime-in-minutes", "Time after which the publisher auto-shuts down.", CommandOptionType.SingleValue)]
                [Required]
                public ushort RuntimeInMinutes { get; set; }

                public async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
                {
                    if (this.ConcurrentPublishersCount < 1 || this.ConcurrentPublishersCount > 1000)
                    {
                        throw new InvalidOperationException($"--publishers should be between 1 and 1000 inclusive.");
                    }

                    if (this.RequestsPerSecondPerPublisher < 1 || this.RequestsPerSecondPerPublisher > 1000)
                    {
                        throw new InvalidOperationException($"--rps-per-publisher should be between 1 and 1000 inclusive.");
                    }

                    Metric.Initialize(this.Root);

                    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action<int, Exception> exit = CreateExitHandler(tcs, console, this.RuntimeInMinutes);
                    var payloadCreator = new PayloadCreator(this.TopicName, this.EventSizeInBytes, this.EventsPerRequest, console);
                    var uri = new Uri(this.Address);
                    PublishWorker[] workers = Enumerable.Range(1, this.ConcurrentPublishersCount)
                        .Select(_ => new PublishWorker(uri, payloadCreator, console, exit))
                        .ToArray();

                    var timer = new Timer(this.OnTimer, (workers, console), Timeout.Infinite, Timeout.Infinite);
                    double intervalMs = 1000 / (double)this.RequestsPerSecondPerPublisher;
                    timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
                    return await tcs.Task;
                }

                private static Action<int, Exception> CreateExitHandler(TaskCompletionSource<int> tcs, IConsole console, int runtimeInMinutes)
                {
                    void exitHandler(int returnCode, Exception ex)
                    {
                        console.WriteLine($"ERR [{DateTime.UtcNow}] {nameof(CreateExitHandler)} was invoked at:\n{new StackTrace(true).ToString()}");

                        if (ex == null)
                        {
                            tcs.TrySetResult(returnCode);
                        }
                        else
                        {
                            tcs.TrySetException(ex);
                        }
                    }

                    Task.Delay(TimeSpan.FromMinutes(runtimeInMinutes)).ContinueWith(t =>
                    {
                        console.WriteLine($"--runtime-in-minutes ({runtimeInMinutes}) Minutes have passed, shutting down publishers.");
                        tcs.TrySetResult(0);
                    },
                    TaskScheduler.Default);

                    return exitHandler;
                }

                private void OnTimer(object state)
                {
                    (PublishWorker[] workers, IConsole console) = ((PublishWorker[], IConsole))state;
                    foreach (PublishWorker worker in workers)
                    {
                        ThreadPool.UnsafeQueueUserWorkItem(worker.PublishFireAndForget, null);
                    }

                    long newCount = Interlocked.Add(ref this.totalRequestsInitiatedCount, workers.Length);
                    console.WriteLine($"INF [{DateTime.UtcNow}] Initiated {workers.Length} publish requests. Total sent={newCount}");
                }

                private long totalRequestsInitiatedCount = 0;
            }
        }
    }
}
