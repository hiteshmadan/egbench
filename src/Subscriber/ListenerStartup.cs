// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics.Counter;
using App.Metrics.Histogram;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using static EGBench.CLI.SubscriberCLI;

namespace EGBench
{
    public class ListenerStartup : IStartup
    {
        private readonly int delayInMs;
        private readonly string eventTimePropertyName;
        private readonly bool logPayloads;
        private readonly HttpStatusCode[] statusCodeMap;
        private readonly int consoleLogIntervalInSeconds;
        private long requestsReceived;
        private long lastLoggedTimestampTicks;

        public ListenerStartup(StartListenerCommand startListenerCommand)
        {
            this.delayInMs = (int)Math.Max(0, startListenerCommand.MeanDelayInMs);
            this.eventTimePropertyName = startListenerCommand.EventTimeJsonPropertyName;
            this.logPayloads = startListenerCommand.LogPayloads;
            this.lastLoggedTimestampTicks = Timestamp.Now.Ticks;
            this.consoleLogIntervalInSeconds = startListenerCommand.Parent.Parent.MetricsIntervalSeconds;
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

        private static void ParseObjectAndLogEventMetrics(JsonElement obj, ListenerStartup @this, DateTimeOffset finishedReading, ICounter events)
        {
            events.Increment();

            bool foundTimeProperty = false;
            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (property.Name.Equals(@this.eventTimePropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.TryGetDateTimeOffset(out DateTimeOffset value))
                    {
                        foundTimeProperty = true;
                        TimeSpan e2eDelay = finishedReading - value;
                        long totalMs = (long)e2eDelay.TotalMilliseconds;
                        if (totalMs > 0)
                        {
                            Metric.SubscribeE2ELatencyMs.Update(totalMs);
                        }

                        break;
                    }
                }
            }

            if (!foundTimeProperty)
            {
                EGBenchLogger.WriteLine($"Not reporting E2E Latency since the time property {@this.eventTimePropertyName} was not found on the payload.");
            }
        }

        private async Task RequestHandlerAsync(HttpContext context)
        {
            Timestamp startTimestamp = Timestamp.Now;
            int resultStatusCode = (int)this.statusCodeMap[ThreadSafeRandom.Next(0, 100)];
            (ICounter eventsMetric, ICounter requestsMetric, IHistogram requestLatencyMetric) = SelectMetrics(resultStatusCode);
            ReadResult result;
            bool resultHasValue = false;
            if (this.logPayloads)
            {
                EGBenchLogger.WriteLine("Headers: " + JsonSerializer.Serialize<IDictionary<string, StringValues>>(context.Request.Headers));
            }

            Interlocked.Increment(ref this.requestsReceived);

            long lastLoggedTicks = this.lastLoggedTimestampTicks;
            Timestamp lastLoggedTimestamp = Timestamp.FromTicks(lastLoggedTicks);
            Timestamp now = Timestamp.Now;

            if (lastLoggedTimestamp.ElapsedSeconds >= this.consoleLogIntervalInSeconds)
            {
                if (lastLoggedTicks == Interlocked.CompareExchange(ref this.lastLoggedTimestampTicks, now.Ticks, lastLoggedTicks))
                {
                    long requestsReceivedLocal = Interlocked.Exchange(ref this.requestsReceived, 0);
                    EGBenchLogger.WriteLine($"Received (success+fail) RPS in last {this.consoleLogIntervalInSeconds} seconds={requestsReceivedLocal / this.consoleLogIntervalInSeconds}");
                }
            }

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

                    if (resultHasValue)
                    {
                        context.Request.BodyReader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                    }
                }

                if (resultHasValue)
                {
                    DateTimeOffset finishedReading = DateTimeOffset.Now;
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    if (this.logPayloads)
                    {
                        EGBenchLogger.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
                    }

                    using (var jsonDoc = JsonDocument.Parse(buffer, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    {
                        switch (jsonDoc.RootElement.ValueKind)
                        {
                            case JsonValueKind.Array:
                                foreach (JsonElement obj in jsonDoc.RootElement.EnumerateArray())
                                {
                                    if (obj.ValueKind == JsonValueKind.Object)
                                    {
                                        ParseObjectAndLogEventMetrics(obj, this, finishedReading, eventsMetric);
                                    }
                                }

                                break;

                            case JsonValueKind.Object:
                                ParseObjectAndLogEventMetrics(jsonDoc.RootElement, this, finishedReading, eventsMetric);
                                break;

                            default:
                                break;
                        }
                    }

                    context.Request.BodyReader.AdvanceTo(result.Buffer.End);
                }

                await context.Request.BodyReader.CompleteAsync();
            }
            catch (Exception ex)
            {
                await context.Request.BodyReader.CompleteAsync(ex);
            }

            if (this.delayInMs > 0)
            {
                await Task.Delay(this.delayInMs);
            }

            requestsMetric.Increment();
            requestLatencyMetric.Update(startTimestamp.ElapsedMilliseconds);
            context.Response.StatusCode = resultStatusCode;
            await context.Response.CompleteAsync();
        }
    }
}
