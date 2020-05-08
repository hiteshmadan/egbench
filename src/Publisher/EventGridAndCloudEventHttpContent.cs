﻿// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        private const string JsonContentType = "application/json; charset=utf-8";
        private static readonly byte[] JsonStartArray = Encoding.UTF8.GetBytes("[");
        private static readonly byte[] JsonEndArray = Encoding.UTF8.GetBytes("]");
        private static readonly byte[] JsonValueDelimiter = Encoding.UTF8.GetBytes(",");
        private static readonly MediaTypeHeaderValue MediaTypeHeaderValue = MediaTypeHeaderValue.Parse(JsonContentType);

        private readonly ReadOnlyMemory<byte> prefixBytes;
        private readonly ReadOnlyMemory<byte> eventTimeBytes;
        private readonly ReadOnlyMemory<byte> postfixBytes;
        private readonly ushort eventsPerRequest;
        private readonly long contentLength;
        private readonly ReadOnlyMemory<byte> postFixCommaPrefixBytes;

        public EventGridAndCloudEventHttpContent(ReadOnlyMemory<byte> prefixBytes, string eventTimeString, ReadOnlyMemory<byte> postfixBytes, ushort eventsPerRequest)
        {
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

            this.Headers.ContentType = MediaTypeHeaderValue;
            this.eventTimeBytes = Encoding.UTF8.GetBytes(eventTimeString).AsMemory();

            long perEventLength = this.prefixBytes.Length + this.eventTimeBytes.Length + this.postfixBytes.Length + JsonValueDelimiter.Length;
            this.contentLength = JsonStartArray.Length + JsonEndArray.Length + (this.eventsPerRequest * perEventLength) - JsonValueDelimiter.Length;
        }

        public async Task SerializeToStreamAsync(Stream stream)
        {
            await stream.WriteAsync(JsonStartArray, 0, JsonStartArray.Length);
            await stream.WriteAsync(this.prefixBytes);
            await stream.WriteAsync(this.eventTimeBytes);

            for (int i = 1; i < this.eventsPerRequest - 1; i++)
            {
                await stream.WriteAsync(this.postFixCommaPrefixBytes);
                await stream.WriteAsync(this.eventTimeBytes);
            }

            await stream.WriteAsync(this.postfixBytes);
            await stream.WriteAsync(JsonEndArray, 0, JsonEndArray.Length);

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
