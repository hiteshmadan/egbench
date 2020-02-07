// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
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
    internal class EventGridHttpContent : HttpContent
    {
        private const string JsonContentType = "application/json; charset=utf-8";
        private static readonly byte[] JsonStartArray = Encoding.UTF8.GetBytes("[");
        private static readonly byte[] JsonEndArray = Encoding.UTF8.GetBytes("]");
        private static readonly byte[] JsonValueDelimiter = Encoding.UTF8.GetBytes(",");
        private static readonly MediaTypeHeaderValue MediaTypeHeaderValue = MediaTypeHeaderValue.Parse(JsonContentType);

        private readonly EventGridPayloadCreator creator;
        private readonly byte[] eventTimeBytes;
        private readonly long length;

        public EventGridHttpContent(EventGridPayloadCreator creator)
        {
            this.creator = creator;
            this.Headers.ContentType = MediaTypeHeaderValue;
            string eventTimeString = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture);
            this.eventTimeBytes = Encoding.UTF8.GetBytes(eventTimeString);

            long perEventLength = this.creator.PrefixBytes.Length + this.eventTimeBytes.Length + this.creator.PostfixBytes.Length + JsonValueDelimiter.Length;
            this.length = JsonStartArray.Length + JsonEndArray.Length + (this.creator.EventsPerRequest * perEventLength) - JsonValueDelimiter.Length;
        }

        public async Task SerializeToStreamAsync(Stream stream)
        {
            await stream.WriteAsync(JsonStartArray, 0, JsonStartArray.Length);
            await stream.WriteAsync(this.creator.PrefixBytes, 0, this.creator.PrefixBytes.Length);
            await stream.WriteAsync(this.eventTimeBytes, 0, this.eventTimeBytes.Length);
            await stream.WriteAsync(this.creator.PostfixBytes, 0, this.creator.PostfixBytes.Length);

            for (int i = 1; i < this.creator.EventsPerRequest; i++)
            {
                await stream.WriteAsync(JsonValueDelimiter, 0, JsonValueDelimiter.Length);
                await stream.WriteAsync(this.creator.PrefixBytes, 0, this.creator.PrefixBytes.Length);
                await stream.WriteAsync(this.eventTimeBytes, 0, this.eventTimeBytes.Length);
                await stream.WriteAsync(this.creator.PostfixBytes, 0, this.creator.PostfixBytes.Length);
            }

            await stream.WriteAsync(JsonEndArray, 0, JsonEndArray.Length);

            await stream.FlushAsync();
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext ctx) => this.SerializeToStreamAsync(stream);

        protected override bool TryComputeLength(out long @length)
        {
            @length = this.length;
            return true;
        }
    }
}
