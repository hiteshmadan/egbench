// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace EGBench
{
    internal class EventGridAndCloudEventHttpContent : HttpContent
    {
        private static readonly ReadOnlyMemory<byte> JsonStartArray = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("["));
        private static readonly ReadOnlyMemory<byte> JsonEndArray = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("]"));
        private static readonly byte[] JsonValueDelimiter = Encoding.UTF8.GetBytes(",");

        private static readonly MediaTypeHeaderValue ContentTypeApplicationJson = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
        private static readonly MediaTypeHeaderValue ContentTypeCloudEventsSingle = MediaTypeHeaderValue.Parse("application/cloudevents+json; charset=utf-8");
        private static readonly MediaTypeHeaderValue ContentTypeCloudEventsBatch = MediaTypeHeaderValue.Parse("application/cloudevents-batch+json; charset=utf-8");

        private readonly ContentType contentType;
        private readonly ReadOnlyMemory<byte> prefixBytes;
        private readonly ReadOnlyMemory<byte> eventTimeBytes;
        private readonly ReadOnlyMemory<byte> postfixBytes;
        private readonly ushort eventsPerRequest;
        private readonly long contentLength;
        private readonly ReadOnlyMemory<byte> postFixCommaPrefixBytes;

        public EventGridAndCloudEventHttpContent(ContentType contentType, ReadOnlyMemory<byte> prefixBytes, string eventTimeString, ReadOnlyMemory<byte> postfixBytes, ushort eventsPerRequest)
        {
            this.contentType = contentType;
            this.prefixBytes = prefixBytes;
            this.postfixBytes = postfixBytes;
            this.eventsPerRequest = eventsPerRequest;

            byte[] postFixCommaPrefixBytes = new byte[this.prefixBytes.Length + JsonValueDelimiter.Length + this.postfixBytes.Length];
            Memory<byte> dest = postFixCommaPrefixBytes.AsMemory();
            postfixBytes.CopyTo(dest);
            dest = dest.Slice(postfixBytes.Length);
            JsonValueDelimiter.CopyTo(dest);
            dest = dest.Slice(JsonValueDelimiter.Length);
            prefixBytes.CopyTo(dest);
            dest = dest.Slice(prefixBytes.Length);
            if (dest.Length != 0)
            {
                throw new InvalidOperationException("Expected the byte[] to get filled up in these three steps. somethings wrong in the math.");
            }

            this.postFixCommaPrefixBytes = postFixCommaPrefixBytes.AsMemory();

            this.Headers.ContentType = contentType switch
            {
                ContentType.ApplicationJson => ContentTypeApplicationJson,
                ContentType.CloudEventsSingle => ContentTypeCloudEventsSingle,
                ContentType.CloudEventsBatch => ContentTypeCloudEventsBatch,
                _ => throw new InvalidOperationException($"Unknown contentType {contentType}")
            };

            this.eventTimeBytes = Encoding.UTF8.GetBytes(eventTimeString).AsMemory();

            long perEventLength = this.prefixBytes.Length + this.eventTimeBytes.Length + this.postfixBytes.Length + JsonValueDelimiter.Length;
            this.contentLength = JsonStartArray.Length + JsonEndArray.Length + (this.eventsPerRequest * perEventLength) - JsonValueDelimiter.Length;
        }

        public async Task SerializeToStreamAsync(Stream stream)
        {
            if (this.contentType == ContentType.CloudEventsSingle)
            {
                await stream.WriteAsync(this.prefixBytes);
                await stream.WriteAsync(this.eventTimeBytes);
                await stream.WriteAsync(this.postfixBytes);
            }
            else
            {
                await stream.WriteAsync(JsonStartArray);
                await stream.WriteAsync(this.prefixBytes);
                await stream.WriteAsync(this.eventTimeBytes);

                for (int i = 1; i < this.eventsPerRequest; i++)
                {
                    await stream.WriteAsync(this.postFixCommaPrefixBytes);
                    await stream.WriteAsync(this.eventTimeBytes);
                }

                await stream.WriteAsync(this.postfixBytes);
                await stream.WriteAsync(JsonEndArray);
            }

            await stream.FlushAsync();
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext ctx) => this.SerializeToStreamAsync(stream);

        protected override bool TryComputeLength(out long @length)
        {
            @length = this.contentLength;
            return true;
        }
    }
}
