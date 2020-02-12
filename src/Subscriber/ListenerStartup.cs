// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SpanJson;
using static EGBench.CLI.SubscriberCLI;

namespace EGBench
{
    public class ListenerStartup : IStartup
    {
        private readonly int delayInMs;
        private readonly HttpStatusCode[] statusCodeMap;

        public ListenerStartup(StartListenerCommand startListenerCommand)
        {
            this.delayInMs = (int)Math.Max(0, startListenerCommand.MeanDelayInMs);
            this.statusCodeMap = new HttpStatusCode[100];

            Span<HttpStatusCode> span = this.statusCodeMap.AsSpan();
            foreach ((int percent, HttpStatusCode code) in startListenerCommand.StatusCodeMap)
            {
                span.Slice(0, percent).Fill(code);
                span = span.Slice(percent);
            }
        }

        public IServiceProvider ConfigureServices(IServiceCollection services) => services.BuildServiceProvider();

        public void Configure(IApplicationBuilder app) => app.Run(this.RequestHandlerAsync);

        private async Task RequestHandlerAsync(HttpContext context)
        {
            Timestamp receiveDuration = Timestamp.Now;
            if (this.delayInMs > 0)
            {
                await Task.Delay(this.delayInMs);
            }

            try
            {
                using (IMemoryOwner<byte> bytes = await context.Request.Body.CopyToPooledMemoryAsync(CancellationToken.None))
                {
                    int numEvents = ParseArray(bytes);
                    if (numEvents > 0)
                    {
                        Metric.SuccessEventsDelivered.Increment(numEvents);
                    }

                    Metric.SuccessRequestsDelivered.Increment();
                    Metric.SuccessDeliveryLatencyMs.Update(receiveDuration.ElapsedMilliseconds);
                    context.Response.StatusCode = (int)this.statusCodeMap[ThreadSafeRandom.Next(0, 100)];
                }
            }
            catch
            {
                Metric.ErrorRequestsDelivered.Increment();
                Metric.ErrorDeliveryLatencyMs.Update(receiveDuration.ElapsedMilliseconds);
                throw;
            }

            static int ParseArray(IMemoryOwner<byte> bytes)
            {
                var reader = new JsonReader<byte>(bytes.Memory.Span);
                int count = 0;
                reader.ReadUtf8BeginArrayOrThrow();
                while (!reader.TryReadUtf8IsEndArrayOrValueSeparator(ref count))
                {
                    reader.SkipNextUtf8Segment();
                }

                return count;
            }
        }
    }
}
