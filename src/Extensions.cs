using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EGBench
{
    internal static class StreamExtensions
    {
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
