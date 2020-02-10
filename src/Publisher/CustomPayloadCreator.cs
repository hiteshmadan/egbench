// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EGBench
{
    internal class CustomPayloadCreator : IPayloadCreator
    {
        private const string JsonContentType = "application/json; charset=utf-8";
        private static readonly MediaTypeHeaderValue MediaTypeHeaderValue = MediaTypeHeaderValue.Parse(JsonContentType);
        private readonly ReadOnlyMemory<byte> bytes;

        public CustomPayloadCreator(string dataPayload, ushort eventsPerRequest, IConsole console)
        {
            if (string.IsNullOrWhiteSpace(dataPayload))
            {
                throw new InvalidOperationException("-d|--data-payload should either be a valid file path, or inline json object that starts with {.");
            }

            string trimmed = dataPayload.Trim();
            if (trimmed.StartsWith('{'))
            {
            }
            else if (File.Exists(trimmed))
            {
                trimmed = File.ReadAllText(trimmed);
            }
            else
            {
                throw new InvalidOperationException("-d|--data-payload should either be a valid file path, or inline json object that starts with {.");
            }

            this.EventsPerRequest = eventsPerRequest;
            byte[] byteArray = new byte[(Encoding.UTF8.GetByteCount(trimmed) * eventsPerRequest) + 2 + (eventsPerRequest - 1)];

            Span<byte> eventBytes = Encoding.UTF8.GetBytes(trimmed).AsSpan();
            Span<byte> dest = byteArray.AsSpan();
            dest[0] = (byte)'[';
            dest = dest.Slice(1);

            for (int i = 0; i < eventsPerRequest; i++)
            {
                eventBytes.CopyTo(dest);
                dest = dest.Slice(eventBytes.Length);

                if (i < eventsPerRequest - 1)
                {
                    dest[0] = (byte)',';
                    dest = dest.Slice(1);
                }
            }

            dest[0] = (byte)']';
            dest = dest.Slice(1);

            if (dest.Length != 0)
            {
                throw new InvalidOperationException("Byte array length calculation / creation logic is busted.");
            }

            this.bytes = new ReadOnlyMemory<byte>(byteArray);

            this.Validate(console);
        }

        public ushort EventsPerRequest { get; }

        public HttpContent CreateHttpContent()
        {
            var httpContent = new ReadOnlyMemoryContent(this.bytes);
            httpContent.Headers.ContentType = MediaTypeHeaderValue;
            return httpContent;
        }

        private void Validate(IConsole console)
        {
            string serialized;
            int bytesLength;
            using (var ms = new MemoryStream())
            using (HttpContent content = this.CreateHttpContent())
            {
                content.CopyToAsync(ms).GetAwaiter().GetResult();
                byte[] bytes = ms.ToArray();
                bytesLength = bytes.Length;
                serialized = Encoding.UTF8.GetString(bytes);
            }

            _ = JsonConvert.DeserializeObject<JArray>(serialized);
            EGBenchLogger.WriteLine(console, $"Sample request payload that'll get sent out. Size={bytesLength} Actual payload=\n\n{serialized}");
        }
    }
}
