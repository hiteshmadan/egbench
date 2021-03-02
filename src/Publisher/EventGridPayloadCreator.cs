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
    internal class EventGridPayloadCreator : IPayloadCreator
    {
        private readonly ReadOnlyMemory<byte> prefixBytes;
        private readonly ReadOnlyMemory<byte> postfixBytes;

        public EventGridPayloadCreator(string topicName, uint eventSizeInBytes, ushort eventsPerRequest, IConsole console)
        {
            this.EventsPerRequest = eventsPerRequest;

            string eventTimeString = GetEventTimeString();

            var eventGridEvent = new EventGridEvent
            {
                Id = "id",
                Topic = topicName,
                Subject = "subject",
                EventTime = eventTimeString,
                EventType = "eventType",
                DataVersion = "v1",
                MetadataVersion = "1",
                Data = new Dictionary<string, string>
                {
                    ["Prop"] = string.Empty
                }
            };

            int envelopeLength = JsonSerializer.SerializeToUtf8Bytes(eventGridEvent).Length;
            int dataBytesLength = (int)Math.Max(1, eventSizeInBytes - envelopeLength);

            eventGridEvent.Data["Prop"] = new string('a', dataBytesLength);

            string serializedEvent = JsonSerializer.Serialize(eventGridEvent);
            int eventTimeHoleOffset = serializedEvent.IndexOf(eventTimeString, StringComparison.Ordinal);
            string prefix = serializedEvent.Substring(0, eventTimeHoleOffset);
            string postfix = serializedEvent.Substring(eventTimeHoleOffset + eventTimeString.Length);
            this.prefixBytes = Encoding.UTF8.GetBytes(prefix).AsMemory();
            this.postfixBytes = Encoding.UTF8.GetBytes(postfix).AsMemory();

            this.Validate(console);
        }

        public ushort EventsPerRequest { get; }

        public HttpContent CreateHttpContent() => new EventGridAndCloudEventHttpContent(ContentType.ApplicationJson, this.prefixBytes, GetEventTimeString(), this.postfixBytes, this.EventsPerRequest);

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
            _ = JsonSerializer.Deserialize<EventGridEvent[]>(serialized);
        }

        private class EventGridEvent
        {
            public string Id { get; set; }

            public string Topic { get; set; }

            public string Subject { get; set; }

            public string EventTime { get; set; }

            public string EventType { get; set; }

            public string DataVersion { get; set; }

            public string MetadataVersion { get; set; }

            public Dictionary<string, string> Data { get; set; }
        }
    }
}
