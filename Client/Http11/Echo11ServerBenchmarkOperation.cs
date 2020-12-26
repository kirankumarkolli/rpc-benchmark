//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Threading.Tasks;

    internal class Echo11ServerBenchmarkOperation : IBenchmarkOperation
    {
        private static HttpClient client;
        private readonly string requestUri;

        private readonly string partitionKeyPath;
        private readonly Dictionary<string, object> sampleJObject;

        public Echo11ServerBenchmarkOperation(BenchmarkConfig config)
        {
            this.partitionKeyPath = config.PartitionKeyPath.Replace("/", "");

            this.requestUri = config.RequestBaseUri().ToString();

            this.sampleJObject = JsonHelper.Deserialize<Dictionary<string, object>>(config.ItemTemplatePayload());
            if (Echo11ServerBenchmarkOperation.client == null)
            {
                Echo11ServerBenchmarkOperation.client = Utility.CreateHttp1Client(config.MaxConnectionsPerServer());
            }
        }

    public async Task ExecuteOnceAsync()
        {
            using (MemoryStream input = JsonHelper.ToStream(this.sampleJObject))
            {
                using (HttpContent httpContent = new StreamContent(input))
                {
                    string targetUri = this.requestUri + Guid.NewGuid().ToString();
                    using (HttpResponseMessage responseMessage = await Echo11ServerBenchmarkOperation.client
                                    .GetAsync(targetUri))
                    {
                        responseMessage.EnsureSuccessStatusCode();
                    }
                }
            }
        }

        public Task PrepareAsync()
        {
            string newPartitionKey = Guid.NewGuid().ToString();

            this.sampleJObject["id"] = Guid.NewGuid().ToString();
            this.sampleJObject[this.partitionKeyPath] = newPartitionKey;

            return Task.CompletedTask;
        }
    }
}
