// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http;

namespace EGBench
{
    internal interface IPayloadCreator
    {
        ushort EventsPerRequest { get; }

        HttpContent CreateHttpContent();
    }
}
