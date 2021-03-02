// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    internal class CloudEvent10PayloadCreator : IPayloadCreator
    {
        private readonly ReadOnlyMemory<byte> prefixBytes;
        private readonly ReadOnlyMemory<byte> postfixBytes;

        public CloudEvent10PayloadCreator(string topicName, uint eventSizeInBytes, ushort eventsPerRequest, IConsole console)
        {
            this.EventsPerRequest = eventsPerRequest;

            string eventTimeString = GetEventTimeString();

            var cloudEvent = new CloudEvent10
            {
                Id = "id",
                Source = topicName,
                Subject = "subject",
                Time = eventTimeString,
                Type = "eventType",
                SpecVersion = "1.0",
                DataContentType = "application/json",
                Data = new Dictionary<string, string>
                {
                    ["Prop"] = string.Empty
                }
            };

            int envelopeLength = JsonSerializer.SerializeToUtf8Bytes(cloudEvent).Length;
            int dataBytesLength = (int)Math.Max(1, eventSizeInBytes - envelopeLength);

            cloudEvent.Data["Prop"] = new string('a', dataBytesLength);

            string serializedEvent = JsonSerializer.Serialize(cloudEvent);
            int eventTimeHoleOffset = serializedEvent.IndexOf(eventTimeString, StringComparison.Ordinal);
            string prefix = serializedEvent.Substring(0, eventTimeHoleOffset);
            string postfix = serializedEvent.Substring(eventTimeHoleOffset + eventTimeString.Length);
            this.prefixBytes = Encoding.UTF8.GetBytes(prefix).AsMemory();
            this.postfixBytes = Encoding.UTF8.GetBytes(postfix).AsMemory();

            this.Validate(console);
        }

        public ushort EventsPerRequest { get; }

        internal byte[] PrefixBytes { get; }

        internal byte[] PostfixBytes { get; }

        public HttpContent CreateHttpContent() => new EventGridAndCloudEventHttpContent(ContentType.CloudEventsBatch, this.prefixBytes, GetEventTimeString(), this.postfixBytes, this.EventsPerRequest);

        private static string GetEventTimeString() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        private void Validate(IConsole console)
        {
            string serialized;
            int bytesLength;
            using (var ms = new MemoryStream())
            using (var content = (EventGridAndCloudEventHttpContent)this.CreateHttpContent())
            {
                content.SerializeToStreamAsync(ms).GetAwaiter().GetResult();
                byte[] bytes = ms.ToArray();
                bytesLength = bytes.Length;
                serialized = Encoding.UTF8.GetString(ms.ToArray());
            }

            EGBenchLogger.WriteLine(console, $"Sample request payload that'll get sent out: Size={bytesLength} Actual payload=\n{serialized}");
            _ = JsonSerializer.Deserialize<CloudEvent10[]>(serialized);
        }

        private class CloudEvent10
        {
            public string Id { get; set; }

            public string Source { get; set; }

            public string Subject { get; set; }

            public string Time { get; set; }

            public string Type { get; set; }

            public string SpecVersion { get; set; }

            public string DataContentType { get; set; }

            public Dictionary<string, string> Data { get; set; }
        }
    }
}
