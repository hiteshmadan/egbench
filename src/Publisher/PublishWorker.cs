// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    internal class PublishWorker
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private static readonly Version Version = new Version("1.1");
        private readonly Uri uri;
        private readonly PayloadCreator payloadCreator;
        private readonly string httpVersion;
        private readonly IConsole console;
        private readonly Action<int, Exception> exit;
        private readonly HttpClient httpClient;

        public PublishWorker(Uri uri, PayloadCreator payloadCreator, string httpVersion, bool skipServerCertificateValidation, IConsole console, Action<int, Exception> exit)
        {
            this.uri = uri;
            this.payloadCreator = payloadCreator;
            this.httpVersion = httpVersion;
            this.console = console;
            this.exit = exit;

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
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                PreAuthenticate = false,
                MaxConnectionsPerServer = int.MaxValue // we manually control concurrency at the request level, instead of asking socketsHttpClient to do it at the connection level.
            };

            if (skipServerCertificateValidation)
            {
                httpHandler.SslOptions.RemoteCertificateValidationCallback = (a, b, c, d) => true;
            }

            this.httpClient = new HttpClient(httpHandler, disposeHandler: true);
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        public async void PublishFireAndForget(object state)
        {
            try
            {
                using (HttpContent content = this.payloadCreator.CreateHttpContent())
                using (var request = new HttpRequestMessage(HttpMethod.Post, this.uri) { Content = content, Version = Version })
                {
                    using (HttpResponseMessage response = await this.httpClient.SendAsync(request))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            EGBenchLogger.WriteLine(this.console, $"HTTP {(int)response.StatusCode} {response.StatusCode} - {response.ReasonPhrase}");
                        }
                        else
                        {
                            Metric.EventsPublished.Increment();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EGBenchLogger.WriteLine(this.console, $"{ex.Message}");

                // unhandled exceptions in async void methods can bring down the process, swallow all exceptions.
                // this.exit(1, ex);
            }
        }
    }
}
