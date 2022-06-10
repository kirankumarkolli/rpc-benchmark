//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using Microsoft.Azure.Cosmos;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;

    internal class Echo20ServerBenchmarkOperation : IBenchmarkOperation
    {
        private static volatile Lazy<HttpClient> client;
        private readonly string requestUri;
        private IComputeHash authKeyHashFunction;

        private readonly string partitionKeyPath;

        public Echo20ServerBenchmarkOperation(BenchmarkConfig config)
        {
            this.partitionKeyPath = config.PartitionKeyPath.Replace("/", "");

            this.requestUri = config.RequestBaseUri().ToString();

            if (client == null) // HACK: Instances are created serially, for now OKEY
            {
                client = new Lazy<HttpClient>(() => Utility.CreateHttp2Client(config.MaxConnectionsPerServer()));
            }

            string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
            authKeyHashFunction = new StringHMACSHA256Hash(authKey);
        }

        public async Task ExecuteOnceAsync()
        {
            string targetUri = this.requestUri;
            using (HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, targetUri))
            {
                // ref: https://github.com/dotnet/runtime/issues/987
                httpRequest.Version = HttpVersion.Version20;
                httpRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

                string dateHeaderValue = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
                httpRequest.Headers.Add(Microsoft.Azure.Documents.HttpConstants.HttpHeaders.XDate, dateHeaderValue);

                string authorization = AuthorizationHelper.GenerateKeyAuthorizationCore("GET",
                    dateHeaderValue,
                    "docs",
                    httpRequest.RequestUri.AbsolutePath.TrimStart(new char[] { '/' }),
                    authKeyHashFunction);
                httpRequest.Headers.TryAddWithoutValidation(Microsoft.Azure.Documents.HttpConstants.HttpHeaders.Authorization, authorization);

                using (HttpResponseMessage responseMessage = await client.Value.SendAsync(httpRequest))
                {
                    responseMessage.EnsureSuccessStatusCode();

                    // Drain the response
                    using (Stream payload = await responseMessage.Content.ReadAsStreamAsync())
                    {
                    }
                }
            }
        }

        public Task PrepareAsync()
        {
            return Task.CompletedTask;
        }

    }
}
