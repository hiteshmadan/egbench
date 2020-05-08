// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    internal static class Extensions
    {
        public static void LogOptionValues(this object @this, IConsole console)
        {
            PropertyInfo[] options = @this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<OptionAttribute>() != null).ToArray();
            EGBenchLogger.WriteLine(console, $"Commandline arguments (merged from cmdline and code defaults):\n{string.Join("\n", options.Select(o => $"{o.Name}={ToString(o.GetValue(@this))}"))}");

            static string ToString(object value)
            {
                if (value is string[] typed)
                {
                    return string.Join(" ", typed);
                }
                else if (value is null)
                {
                    return "NULL";
                }
                else
                {
                    return value.ToString();
                }
            }
        }

        public static void GrowRented<T>(this MemoryPool<T> pool, ref IMemoryOwner<T> rented)
        {
            IMemoryOwner<T> toReturn = rented;
            rented = pool.Rent(rented.Memory.Length * 2);
            toReturn.Memory.Span.CopyTo(rented.Memory.Span);
            toReturn.Dispose();
        }

        public static IMemoryOwner<T> Slice<T>(this IMemoryOwner<T> original, int startIndex, int length)
        {
            if (startIndex == 0 && length == original.Memory.Length)
            {
                return original;
            }
            else
            {
                return new MemoryOwnerSlice<T>(original, startIndex, length);
            }
        }

        public static async Task<IMemoryOwner<byte>> CopyToPooledMemoryAsync(this Stream requestStream, CancellationToken token, int contentLengthHint = 0, MemoryPool<byte> pool = default)
        {
            if (pool == null)
            {
                pool = MemoryPool<byte>.Shared;
            }

            IMemoryOwner<byte> bytes = pool.Rent(contentLengthHint > 0 ? contentLengthHint : 4096);

            try
            {
                int read = 0, totalSize = 0;
                Memory<byte> destination = bytes.Memory;
                while ((read = await requestStream.ReadAsync(destination, token)) > 0)
                {
                    int newLength = totalSize + read;
                    if (read == destination.Length)
                    {
                        pool.GrowRented(ref bytes);
                        destination = bytes.Memory.Slice(newLength);
                    }
                    else
                    {
                        destination = destination.Slice(read);
                    }

                    totalSize = newLength;
                }

                if (totalSize == 0)
                {
                    bytes.Dispose();
                    return null;
                }
                else
                {
                    return bytes.Slice(0, totalSize);
                }
            }
            catch
            {
                bytes?.Dispose();
                throw;
            }
        }
    }
}
