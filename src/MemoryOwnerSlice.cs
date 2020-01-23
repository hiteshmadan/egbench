// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;

namespace EGBench
{
    public sealed class MemoryOwnerSlice<T> : IMemoryOwner<T>
    {
        private IMemoryOwner<T> inner;
        private Memory<T> memory;
        private bool disposed = false;

        public MemoryOwnerSlice(IMemoryOwner<T> inner, int startIndex, int length)
        {
            this.inner = inner;
            this.memory = inner.Memory.Slice(startIndex, length);
        }

        public Memory<T> Memory
        {
            get
            {
                lock (this)
                {
                    if (!this.disposed)
                    {
                        return this.memory;
                    }
                    else
                    {
                        throw new ObjectDisposedException(nameof(MemoryOwnerSlice<T>));
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (this)
            {
                if (!this.disposed)
                {
                    this.inner.Dispose();
                    this.memory = null;
                    this.inner = null;
                    this.disposed = true;
                }
            }
        }
    }
}
