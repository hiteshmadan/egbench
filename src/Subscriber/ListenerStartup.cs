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
        private readonly uint delayInMs;

        public ListenerStartup(StartListenerCommand startListenerCommand)
        {
            this.delayInMs = startListenerCommand.DelayInMs ?? 0;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services) => services.BuildServiceProvider();

        public void Configure(IApplicationBuilder app) => app.Run(this.RequestHandlerAsync);

        private async Task RequestHandlerAsync(HttpContext context)
        {
            if (this.delayInMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(this.delayInMs));
            }

            using (IMemoryOwner<byte> bytes = await context.Request.Body.CopyToPooledMemoryAsync(CancellationToken.None))
            {
                ParseArray(bytes);
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;

            void ParseArray(IMemoryOwner<byte> bytes)
            {
                var reader = new JsonReader<byte>(bytes.Memory.Span);
                int count = 0;
                reader.ReadUtf8BeginArrayOrThrow();
                while (!reader.TryReadUtf8IsEndArrayOrValueSeparator(ref count))
                {
                    reader.SkipNextUtf8Segment();
                }

                if (count > 0)
                {
                    Metric.EventsReceived.Increment(count);
                }
            }
        }
    }
}
