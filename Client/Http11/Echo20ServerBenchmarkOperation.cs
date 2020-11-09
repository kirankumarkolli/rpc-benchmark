//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;

    internal class Echo20ServerBenchmarkOperation : IBenchmarkOperation
    {
        private readonly HttpClient client;
        private readonly Uri requestUri;

        private readonly string partitionKeyPath;
        private readonly Dictionary<string, object> sampleJObject;

        public Echo20ServerBenchmarkOperation(BenchmarkConfig config)
        {
            this.partitionKeyPath = config.PartitionKeyPath.Replace("/", "");

            this.requestUri = config.requestUri();

            this.sampleJObject = JsonHelper.Deserialize<Dictionary<string, object>>(config.ItemTemplatePayload());
            this.client = Echo11ServerBenchmarkOperation.CreateHttpClient(config.MaxConnectionsPerServer());
        }

        public async Task ExecuteOnceAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, this.requestUri)
            {
                Version = new Version(2, 0)
            };

            using (HttpResponseMessage responseMessage = await this.client.SendAsync(req))
            {
                responseMessage.EnsureSuccessStatusCode();
            }
        }

        public Task PrepareAsync()
        {
            string newPartitionKey = Guid.NewGuid().ToString();

            this.sampleJObject["id"] = Guid.NewGuid().ToString();
            this.sampleJObject[this.partitionKeyPath] = newPartitionKey;

            return Task.CompletedTask;
        }


        public static HttpClient CreateHttpClient(int MaxConnectionsPerServer)
        {
            HttpClientHandler httpClientHandler = new HttpClientHandler
            {
                MaxConnectionsPerServer = MaxConnectionsPerServer,
            };

            HttpClient client = new HttpClient(httpClientHandler, disposeHandler: true)
            {
                // DefaultRequestVersion = new Version("2.0")
            };

            client.DefaultRequestHeaders
                  .Accept
                  .Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header

            return client;
        }

    }
}
