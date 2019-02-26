using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace EGBench
{
    public class PayloadCreator
    {
        private readonly string serializedEvent;
        private readonly int eventTimeHoleOffset;
        private readonly string prefix;
        private readonly string postfix;
        private readonly byte[] prefixBytes;
        private readonly byte[] postfixBytes;
        private readonly ushort eventsPerRequest;

        public PayloadCreator(string topicName, uint eventSizeInBytes, ushort eventsPerRequest, IConsole console)
        {
            this.eventsPerRequest = eventsPerRequest;

            string eventTimeString = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture);

            // TODO: Switch on args.TopicSchema when multiple topic schemas are supported
            var envelope = new
            {
                Id = "id",
                Topic = topicName,
                Subject = "subject",
                EventTime = eventTimeString,
                EventType = "eventType",
                DataVersion = "v1",
                MetadataVersion = "1",
                Data = new
                {
                    Prop = string.Empty
                }
            };

            int envelopeLength = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(envelope, Formatting.None));
            int dataBytesLength = (int)Math.Max(1, eventSizeInBytes - envelopeLength);

            var eventGridEvent = new
            {
                envelope.Id,
                envelope.Topic,
                envelope.Subject,
                envelope.EventTime,
                envelope.EventType,
                envelope.DataVersion,
                envelope.MetadataVersion,
                Data = new
                {
                    Prop = new string(Enumerable.Range(1, dataBytesLength).Select(_ => 'a').ToArray())
                }
            };

            this.serializedEvent = JsonConvert.SerializeObject(eventGridEvent, Formatting.None);
            this.eventTimeHoleOffset = this.serializedEvent.IndexOf(eventTimeString, StringComparison.Ordinal);
            this.prefix = this.serializedEvent.Substring(0, this.eventTimeHoleOffset);
            this.postfix = this.serializedEvent.Substring(this.eventTimeHoleOffset + eventTimeString.Length);
            this.prefixBytes = Encoding.UTF8.GetBytes(this.prefix);
            this.postfixBytes = Encoding.UTF8.GetBytes(this.postfix);

            string serialized;
            int bytesLength;
            using (var ms = new MemoryStream())
            using (var content = new EventBatchHttpContent(this))
            {
                content.SerializeToStreamAsync(ms).GetAwaiter().GetResult();
                byte[] bytes = ms.ToArray();
                bytesLength = bytes.Length;
                serialized = Encoding.UTF8.GetString(bytes);
            }
            var eventGridEventArray = new[] { eventGridEvent };
            var deserializedEvent = JsonConvert.DeserializeAnonymousType(serialized, eventGridEventArray);

            console.WriteLine($"INF [{DateTime.UtcNow}] Sample request payload that'll get sent out, starting publishing in 3 seconds. Request Body size={bytesLength}\n{serialized}");
            Thread.Sleep(3000);
        }

        public HttpContent CreateHttpContent() => new EventBatchHttpContent(this);

        private class EventBatchHttpContent : HttpContent
        {
            private const string JsonContentType = "application/json; charset=utf-8";
            private static readonly byte[] JsonStartArray = Encoding.UTF8.GetBytes("[");
            private static readonly byte[] JsonEndArray = Encoding.UTF8.GetBytes("]");
            private static readonly byte[] JsonValueDelimiter = Encoding.UTF8.GetBytes(",");
            private static readonly MediaTypeHeaderValue mediaTypeHeaderValue = MediaTypeHeaderValue.Parse(JsonContentType);

            private readonly PayloadCreator creator;
            private readonly byte[] eventTimeBytes;

            public EventBatchHttpContent(PayloadCreator creator)
            {
                this.creator = creator;
                this.Headers.ContentType = mediaTypeHeaderValue;
                string eventTimeString = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture);
                this.eventTimeBytes = Encoding.UTF8.GetBytes(eventTimeString);
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => this.SerializeToStreamAsync(stream);

            public async Task SerializeToStreamAsync(Stream stream)
            {
                await stream.WriteAsync(JsonStartArray, 0, JsonStartArray.Length);
                await stream.WriteAsync(this.creator.prefixBytes, 0, this.creator.prefixBytes.Length);
                await stream.WriteAsync(this.eventTimeBytes, 0, this.eventTimeBytes.Length);
                await stream.WriteAsync(this.creator.postfixBytes, 0, this.creator.postfixBytes.Length);

                for (int i = 1; i < this.creator.eventsPerRequest; i++)
                {
                    await stream.WriteAsync(JsonValueDelimiter, 0, JsonValueDelimiter.Length);
                    await stream.WriteAsync(this.creator.prefixBytes, 0, this.creator.prefixBytes.Length);
                    await stream.WriteAsync(this.eventTimeBytes, 0, this.eventTimeBytes.Length);
                    await stream.WriteAsync(this.creator.postfixBytes, 0, this.creator.postfixBytes.Length);
                }

                await stream.WriteAsync(JsonEndArray, 0, JsonEndArray.Length);

                await stream.FlushAsync();
            }

            protected override bool TryComputeLength(out long length)
            {
                int perEventLength = this.creator.prefixBytes.Length + this.eventTimeBytes.Length + this.creator.postfixBytes.Length + JsonValueDelimiter.Length;
                length = JsonStartArray.Length + JsonEndArray.Length + (this.creator.eventsPerRequest * perEventLength) - JsonValueDelimiter.Length;
                return true;
            }
        }
    }
}
