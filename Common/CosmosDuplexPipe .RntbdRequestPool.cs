//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using Microsoft.Azure.Documents;
    using System.Collections.Concurrent;

    internal partial class CosmosDuplexPipe : IDisposable
    {
        internal class RntbdRequestPool
        {
            public static readonly RntbdRequestPool Instance = new RntbdRequestPool();

            private readonly ConcurrentQueue<RntbdConstants.Request> requests = new ConcurrentQueue<RntbdConstants.Request>();

            private RntbdRequestPool()
            {
            }

            public RequestOwner Get()
            {
                if (this.requests.TryDequeue(out RntbdConstants.Request request))
                {
                    return new RequestOwner(request);
                }

                return new RequestOwner(new RntbdConstants.Request());
            }

            private void Return(RntbdConstants.Request request)
            {
                request.Reset();
                this.requests.Enqueue(request);
            }

            public readonly struct RequestOwner : IDisposable
            {
                public RequestOwner(RntbdConstants.Request request)
                {
                    this.Request = request;
                }

                public RntbdConstants.Request Request { get; }

                public void Dispose()
                {
                    RntbdRequestPool.Instance.Return(this.Request);
                }
            }
        }
    }
}
