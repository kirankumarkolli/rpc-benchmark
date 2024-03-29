﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CosmosBenchmark
{
    internal sealed class LimitedPipeWriter : IDisposable
    {
        internal readonly PipeWriter pipeWriter;
        private readonly SemaphoreSlim semaphore;

        public LimitedPipeWriter(
            PipeWriter pipeWriter,
            int concurrencyLimit = 1)
        {
            this.pipeWriter = pipeWriter;
            this.semaphore = new SemaphoreSlim(concurrencyLimit, concurrencyLimit);
        }

        public PipeWriter PipeWriter => this.pipeWriter;

        public void Dispose()
        {
            this.semaphore.Dispose();
        }

        public async ValueTask GetMemoryAndFlushAsync(
            int allocationLength,
            Action<Memory<byte>> actOnMemory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await this.semaphore.WaitAsync(cancellationToken);
                Memory<byte> bytes = this.pipeWriter.GetMemory(allocationLength);
                actOnMemory(bytes);
                this.pipeWriter.Advance(allocationLength);
                await this.pipeWriter.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("LimitedPipeWriter" + ex.ToString());
                throw;
            }
            finally
            {
                this.semaphore.Release();
            }
        }
    }
}
