﻿// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    public partial class CLI
    {
        [Command(Name = "publisher", Description = "Generate load against an eventgrid endpoint.")]
        [Subcommand(typeof(StartPublishCommand))]
        public class PublisherCLI
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
            public class StartPublishCommand
            {
                private long totalRequestsInitiatedCount = 0;
                private Timestamp lastLoggedTimestamp = Timestamp.Now;

                public PublisherCLI Parent { get; set; }

                public CLI Root => this.Parent.Parent;

                [Option("-u|--topic-url", "URL to which events should be posted to.", CommandOptionType.SingleValue)]
                [Required]
                public string Address { get; set; }

                [Option("-n|--topic-name", "String that should be used for stamping eventgrid event envelope's Topic field.", CommandOptionType.SingleValue)]
                [Required]
                public string TopicName { get; set; }

                [Option("-s|--topic-schema", "Defaults to EventGrid. Possible values: EventGrid / CloudEventV10 / Custom. Specify the -d|--data-payload property for Custom topic schema.", CommandOptionType.SingleValue)]
                public string TopicSchema { get; set; } = "EventGrid";

                [Option("-p|--data-payload", "Specify the data payload when -s|--topic-schema=Custom. Either give inline json or a file path.", CommandOptionType.SingleValue)]
                public string DataPayload { get; set; }

                [Option("-p|--publishers", "Number of concurrent publishing \"threads\", defaults to 10.", CommandOptionType.SingleValue)]
                public short ConcurrentPublishersCount { get; set; } = 10;

                [Option("-r|--rps-per-publisher", "Requests per second generated by each publish \"thread\", defaults to 10.", CommandOptionType.SingleValue)]
                public short RequestsPerSecondPerPublisher { get; set; } = 10;

                [Option("-e|--events-per-request", "Number of events in each request, defaults to 10.", CommandOptionType.SingleValue)]
                public ushort EventsPerRequest { get; set; } = 10;

                [Option("-b|--event-size-in-bytes", "Number of bytes per event, defaults to 1024, doesn't take effect if -s|--topic-schema==Custom. Total request payload size = 2 + (EventsPerRequest * (EventSizeInBytes + 1) - 1)", CommandOptionType.SingleValue)]
                public uint EventSizeInBytes { get; set; } = 1024;

                [Option("-t|--runtime-in-minutes", "Time after which the publisher auto-shuts down, defaults to 60 minutes.", CommandOptionType.SingleValue)]
                public ushort RuntimeInMinutes { get; set; } = 60;

                [Option("-v|--protocol-version", "The protocol version to use, defaults to 1.1.", CommandOptionType.SingleValue)]
                public string HttpVersion { get; set; } = "1.1";

                [Option("|--skip-ssl-validation", "Skip SSL Server Certificate validation, defaults to false.", CommandOptionType.SingleValue)]
                public bool SkipServerCertificateValidation { get; set; } = false;

                public async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
                {
                    PropertyInfo[] options = this.GetType().GetProperties(BindingFlags.Public).Where(p => p.GetCustomAttribute<OptionAttribute>() != null).ToArray();
                    EGBenchLogger.WriteLine(console, $"Publisher arguments (merged from cmdline and code defaults): {string.Join("|", options.Select(o => $"{o.Name}={o.GetValue(this).ToString()}"))}");

                    if (this.ConcurrentPublishersCount < 1)
                    {
                        throw new InvalidOperationException($"-p|--publishers should be greater than 1.");
                    }

                    if (this.RequestsPerSecondPerPublisher < 1 || this.RequestsPerSecondPerPublisher > 1000)
                    {
                        throw new InvalidOperationException($"-r|--rps-per-publisher should be between 1 and 1000 inclusive");
                    }

                    IPayloadCreator payloadCreator = this.TopicSchema.ToUpperInvariant() switch
                    {
                        "EVENTGRID" => new EventGridPayloadCreator(this.TopicName, this.EventSizeInBytes, this.EventsPerRequest, console),
                        "CUSTOM" => new CustomPayloadCreator(this.DataPayload, this.EventsPerRequest, console),
                        _ => throw new NotImplementedException($"Unknown topic schema {this.TopicSchema}")
                    };

                    Metric.Initialize(this.Root);
                    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action<int, Exception> exit = this.CreateExitHandler(tcs, console, this.RuntimeInMinutes);
                    var uri = new Uri(this.Address);

                    PublishWorker[] workers = Enumerable.Range(1, this.ConcurrentPublishersCount)
                        .Select(_ => new PublishWorker(uri, payloadCreator, this.HttpVersion, this.SkipServerCertificateValidation, console, exit))
                        .ToArray();

                    double intervalMs = 1000 / (double)this.RequestsPerSecondPerPublisher;

                    EGBenchLogger.WriteLine(console, $"{nameof(intervalMs)}={intervalMs} ms | Overall RPS={this.ConcurrentPublishersCount * this.RequestsPerSecondPerPublisher}");
                    using (var timer = new Timer(this.OnTimer, (workers, console), Timeout.Infinite, Timeout.Infinite))
                    {
                        timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMs));
                        return await tcs.Task;
                    }
                }

                private void OnTimer(object state)
                {
                    (PublishWorker[] workers, IConsole console) = ((PublishWorker[], IConsole))state;
                    foreach (PublishWorker worker in workers)
                    {
                        ThreadPool.UnsafeQueueUserWorkItem(worker.PublishFireAndForget, null);
                    }

                    long newCount = Interlocked.Add(ref this.totalRequestsInitiatedCount, workers.Length);
                    if (this.lastLoggedTimestamp.ElapsedSeconds >= 60)
                    {
                        this.lastLoggedTimestamp = Timestamp.Now;
                        EGBenchLogger.WriteLine(console, $"{nameof(this.totalRequestsInitiatedCount)}={newCount}");
                    }
                }

                private Action<int, Exception> CreateExitHandler(TaskCompletionSource<int> tcs, IConsole console, int runtimeInMinutes)
                {
                    void ExitHandler(int returnCode, Exception ex)
                    {
                        EGBenchLogger.WriteLine(console, $"{nameof(this.CreateExitHandler)} was invoked at stacktrace:\n{new StackTrace(true).ToString()}");

                        if (ex == null)
                        {
                            tcs.TrySetResult(returnCode);
                        }
                        else
                        {
                            tcs.TrySetException(ex);
                        }
                    }

                    _ = Task.Delay(TimeSpan.FromMinutes(runtimeInMinutes)).ContinueWith(
                        t =>
                        {
                            EGBenchLogger.WriteLine(console, $"--runtime-in-minutes ({runtimeInMinutes}) Minutes have passed, stopping the process.");
                            tcs.TrySetResult(0);
                        },
                        TaskScheduler.Default);

                    return ExitHandler;
                }
            }
        }
    }
}
