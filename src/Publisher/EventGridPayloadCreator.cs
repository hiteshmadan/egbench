// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace EGBench
{
    internal class EventGridPayloadCreator : IPayloadCreator
    {
        private readonly string serializedEvent;
        private readonly int eventTimeHoleOffset;
        private readonly string prefix;
        private readonly string postfix;

        public EventGridPayloadCreator(string topicName, uint eventSizeInBytes, ushort eventsPerRequest, IConsole console)
        {
            this.EventsPerRequest = eventsPerRequest;

            string eventTimeString = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture);

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
                    Prop = new string('a', dataBytesLength)
                }
            };

            this.serializedEvent = JsonConvert.SerializeObject(eventGridEvent, Formatting.None);
            this.eventTimeHoleOffset = this.serializedEvent.IndexOf(eventTimeString, StringComparison.Ordinal);
            this.prefix = this.serializedEvent.Substring(0, this.eventTimeHoleOffset);
            this.postfix = this.serializedEvent.Substring(this.eventTimeHoleOffset + eventTimeString.Length);
            this.PrefixBytes = Encoding.UTF8.GetBytes(this.prefix);
            this.PostfixBytes = Encoding.UTF8.GetBytes(this.postfix);

            this.Validate(eventGridEvent, console);
        }

        internal byte[] PrefixBytes { get; }

        internal byte[] PostfixBytes { get; }

        internal ushort EventsPerRequest { get; }

        public HttpContent CreateHttpContent() => new EventGridHttpContent(this);

        private void Validate(object eventGridEvent, IConsole console)
        {
            string serialized;
            int bytesLength;
            using (var ms = new MemoryStream())
            using (var content = new EventGridHttpContent(this))
            {
                content.SerializeToStreamAsync(ms).GetAwaiter().GetResult();
                byte[] bytes = ms.ToArray();
                bytesLength = bytes.Length;
                serialized = Encoding.UTF8.GetString(bytes);
            }

            object[] eventGridEventArray = new[] { eventGridEvent };
            _ = JsonConvert.DeserializeAnonymousType(serialized, eventGridEventArray);
            EGBenchLogger.WriteLine(console, $"Sample request payload that'll get sent out: Size={bytesLength} Actual payload=\n{serialized}");
        }
    }
}
