// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using App.Metrics.Counter;
using App.Metrics.Histogram;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static EGBench.CLI.SubscriberCLI;

namespace EGBench
{
    public class ListenerStartup : IStartup
    {
        private readonly int delayInMs;
        private readonly string eventTimePropertyName;
        private readonly bool logPayloads;
        private readonly HttpStatusCode[] statusCodeMap;

        public ListenerStartup(StartListenerCommand startListenerCommand)
        {
            this.delayInMs = (int)Math.Max(0, startListenerCommand.MeanDelayInMs);
            this.eventTimePropertyName = startListenerCommand.EventTimeJsonPropertyName;
            this.logPayloads = startListenerCommand.LogPayloads;

            this.statusCodeMap = new HttpStatusCode[100];
            Span<HttpStatusCode> span = this.statusCodeMap.AsSpan();
            foreach ((int percent, HttpStatusCode code) in startListenerCommand.StatusCodeMap)
            {
                span.Slice(0, percent).Fill(code);
                span = span.Slice(percent);
            }
        }

        public IServiceProvider ConfigureServices(IServiceCollection services) => services.BuildServiceProvider();

        public void Configure(IApplicationBuilder app)
        {
            app.Use(async (HttpContext ctx, Func<Task> next) =>
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    EGBenchLogger.WriteLine(ex.ToString());
                    throw;
                }
            });
            app.Run(this.RequestHandlerAsync);
        }

        private static (ICounter eventsMetric, ICounter requestsMetric, IHistogram requestLatencyMetric) SelectMetrics(int resultStatusCode)
        {
            return ((int)(resultStatusCode / 100)) switch
            {
                int _ when resultStatusCode < 400 => (Metric.SubscribeEventsSuccess, Metric.SubscribeRequestsSuccess, Metric.SubscribeRequestLatencyMsSuccess),
                4 => (Metric.SubscribeEventsUserError, Metric.SubscribeRequestsUserError, Metric.SubscribeRequestLatencyMsUserError),
                _ => (Metric.SubscribeEventsSystemError, Metric.SubscribeRequestsSystemError, Metric.SubscribeRequestLatencyMsSystemError),
            };
        }

        private async Task RequestHandlerAsync(HttpContext context)
        {
            Timestamp startTimestamp = Timestamp.Now;
            int resultStatusCode = (int)this.statusCodeMap[ThreadSafeRandom.Next(0, 100)];
            (ICounter eventsMetric, ICounter requestsMetric, IHistogram requestLatencyMetric) = SelectMetrics(resultStatusCode);
            ReadResult result = default;
            bool resultHasValue = false;
            try
            {
                while (true)
                {
                    result = await context.Request.BodyReader.ReadAsync();
                    resultHasValue |= !result.Buffer.IsEmpty;

                    if (result.IsCanceled)
                    {
                        throw new OperationCanceledException("reading was canceled");
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }

                    context.Request.BodyReader.AdvanceTo(result.Buffer.Start);
                }

                if (resultHasValue)
                {
                    DateTimeOffset finishedReading = DateTimeOffset.Now;
                    this.LogBuffer(result.Buffer);
                    using (var jsonDoc = JsonDocument.Parse(result.Buffer, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    {
                        context.Request.BodyReader.AdvanceTo(result.Buffer.End);

                        switch (jsonDoc.RootElement.ValueKind)
                        {
                            case JsonValueKind.Array:
                                foreach (JsonElement obj in jsonDoc.RootElement.EnumerateArray())
                                {
                                    if (obj.ValueKind == JsonValueKind.Object)
                                    {
                                        this.ParseObjectAndLogEventMetrics(obj, this, finishedReading, eventsMetric);
                                    }
                                }

                                break;

                            case JsonValueKind.Object:
                                this.ParseObjectAndLogEventMetrics(jsonDoc.RootElement, this, finishedReading, eventsMetric);

                                break;

                            default:
                                break;
                        }
                    }
                }

                if (this.delayInMs > 0)
                {
                    await Task.Delay(this.delayInMs);
                }

                requestsMetric.Increment();
                requestLatencyMetric.Update(startTimestamp.ElapsedMilliseconds);
                context.Response.StatusCode = resultStatusCode;
            }
            catch
            {
                if (resultHasValue)
                {
                    context.Request.BodyReader.AdvanceTo(result.Buffer.End);
                }

                throw;
            }
        }

        private void ParseObjectAndLogEventMetrics(JsonElement obj, ListenerStartup @this, DateTimeOffset finishedReading, ICounter events)
        {
            events.Increment();

            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (property.Name.Equals(@this.eventTimePropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.TryGetDateTimeOffset(out DateTimeOffset value))
                    {
                        TimeSpan e2eDelay = finishedReading - value;
                        long totalMs = (long)e2eDelay.TotalMilliseconds;
                        if (totalMs > 0)
                        {
                            Metric.SubscribeE2ELatencyMs.Update(totalMs);
                        }
                    }
                }
            }
        }

        private void LogBuffer(ReadOnlySequence<byte> buffer)
        {
            if (this.logPayloads)
            {
                EGBenchLogger.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
            }
        }
    }
}
