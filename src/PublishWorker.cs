using System;
using System.Net;
using System.Net.Http;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public class PublishWorker
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private readonly IConsole console;
        private readonly HttpClient httpClient;
        private readonly Uri uri;
        private readonly PayloadCreator payloadCreator;
        private readonly Action<int, Exception> exit;

        public PublishWorker(Uri uri, PayloadCreator payloadCreator, IConsole console, Action<int, Exception> exit)
        {
            this.console = console;
            this.httpClient = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = int.MaxValue });
            this.uri = uri;
            this.payloadCreator = payloadCreator;
            this.exit = exit;
        }

        public async void PublishFireAndForget(object state)
        {
            try
            {
                using (HttpContent content = this.payloadCreator.CreateHttpContent())
                using (var request = new HttpRequestMessage(HttpMethod.Post, this.uri) { Content = content })
                {
                    using (HttpResponseMessage response = await this.httpClient.SendAsync(request))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            this.console.WriteLine($"WRN [{DateTime.UtcNow}] HTTP {(int)response.StatusCode} {response.StatusCode} - {response.ReasonPhrase}");
                        }
                    }
                }
            }
            catch (Exception)
            {
                this.console.WriteLine($"ERR [{DateTime.UtcNow}] ex.Message");
                // unhandled exceptions in async void methods can bring down the process, swallow all exceptions.
                // this.exit(1, ex);
            }
        }
    }
}
