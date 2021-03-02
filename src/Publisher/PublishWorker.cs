// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using App.Metrics.Counter;
using App.Metrics.Histogram;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    internal class PublishWorker : IDisposable
    {
        private readonly Uri uri;
        private readonly IPayloadCreator payloadCreator;
        private readonly Version httpVersion;
        private readonly bool logErrors;
        private readonly IConsole console;
        private readonly Action<int, Exception> exit;
        private readonly HttpClient httpClient;
        private readonly Channel<long> buffer;
        private readonly SemaphoreSlim maxConcurrentRequests;

        public PublishWorker(CLI.PublisherCLI.StartPublishCommand startPublishCmd, IPayloadCreator payloadCreator, IConsole console, Action<int, Exception> exit)
        {
            this.uri = new Uri(startPublishCmd.Address);
            this.payloadCreator = payloadCreator;
            this.httpVersion = new Version(startPublishCmd.HttpVersion);
            this.logErrors = startPublishCmd.LogErrors;
            this.console = console;
            this.exit = exit;
            this.maxConcurrentRequests = new SemaphoreSlim(startPublishCmd.MaxConcurrentRequestsPerPublisher);
            this.buffer = Channel.CreateBounded<long>(new BoundedChannelOptions(startPublishCmd.RequestsPerSecondPerPublisher * 10)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

#pragma warning disable CA2000 // Dispose objects before losing scope
            var httpHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseProxy = false,
                UseCookies = false,

                // the following are the defaults too, just making it obvious here for better code readability

                ConnectTimeout = Timeout.InfiniteTimeSpan, // do NOT set this up manually, the passed in cancellation token when making the first request gets honored as connect timeout too. See comment below near new HttpClient().
                Credentials = null,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(30),
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                PreAuthenticate = false,
                MaxConnectionsPerServer = int.MaxValue // we manually control concurrency at the request level, instead of asking socketsHttpClient to do it at the connection level.
            };

            if (startPublishCmd.SkipServerCertificateValidation)
            {
                httpHandler.SslOptions.RemoteCertificateValidationCallback = (a, b, c, d) => true;
            }

            this.httpClient = new HttpClient(httpHandler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            _ = Task.Run(this.PublishLoopAsync);
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        public void Dispose()
        {
            this.httpClient?.Dispose();
            this.maxConcurrentRequests?.Dispose();
        }

        public bool TryEnqueue(long iteration) => this.buffer.Writer.TryWrite(iteration);

        private static (ICounter eventsMetric, ICounter requestsMetric, IHistogram requestLatencyMetric) SelectMetrics(int resultStatusCode)
        {
            return ((int)(resultStatusCode / 100)) switch
            {
                int _ when resultStatusCode < 400 => (Metric.PublishEventsSuccess, Metric.PublishRequestsSuccess, Metric.PublishRequestLatencyMsSuccess),
                4 => (Metric.PublishEventsUserError, Metric.PublishRequestsUserError, Metric.PublishRequestLatencyMsUserError),
                _ => (Metric.PublishEventsSystemError, Metric.PublishRequestsSystemError, Metric.PublishRequestLatencyMsSystemError),
            };
        }

        private async Task PublishLoopAsync()
        {
            while (true)
            {
                while (this.buffer.Reader.TryRead(out long iteration))
                {
                    await this.maxConcurrentRequests.WaitAsync();
                    ThreadPool.UnsafeQueueUserWorkItem(this.PublishFireAndForget, iteration, true);
                }

                await this.buffer.Reader.WaitToReadAsync();
            }
        }

        private async void PublishFireAndForget(long iteration)
        {
            try
            {
                // TODO: Use publishWorkerId+iteration to seed the event.id parameter.
                using (HttpContent content = this.payloadCreator.CreateHttpContent())
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                using (var request = new HttpRequestMessage(HttpMethod.Post, this.uri) { Content = content, Version = this.httpVersion })
                {
                    Timestamp sendDuration = Timestamp.Now;
                    using (HttpResponseMessage response = await this.httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token))
                    {
                        (ICounter eventsMetric, ICounter requestsMetric, IHistogram requestLatencyMetric) = SelectMetrics((int)response.StatusCode);
                        requestLatencyMetric.Update(sendDuration.ElapsedMilliseconds);
                        eventsMetric.Increment(this.payloadCreator.EventsPerRequest);
                        requestsMetric.Increment();

                        if (this.logErrors && response.StatusCode != HttpStatusCode.OK)
                        {
                            EGBenchLogger.WriteLine(this.console, $"HTTP {(int)response.StatusCode}. ReasonPhrase={response.ReasonPhrase} ResponseContent={await response.Content.ReadAsStringAsync()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Metric.PublishRequestsFailed.Increment();
                EGBenchLogger.WriteLine(this.console, $"this.uri={this.uri} ex.Message={ex.Message}");

                // unhandled exceptions in async void methods can bring down the process, swallow all exceptions.
                // this.exit(1, ex);
            }
            finally
            {
                this.maxConcurrentRequests.Release();
            }
        }
    }
}
