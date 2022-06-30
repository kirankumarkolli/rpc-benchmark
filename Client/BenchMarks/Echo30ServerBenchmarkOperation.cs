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

    internal class Echo30ServerBenchmarkOperation : IBenchmarkOperation
    {
        private static readonly byte[] buffer = new byte[4096];
        private HttpClient client;
        private readonly string requestUri;
        private IComputeHash authKeyHashFunction;

        private readonly string partitionKeyPath;

        public Echo30ServerBenchmarkOperation(BenchmarkConfig config)
        {
            this.partitionKeyPath = config.PartitionKeyPath.Replace("/", "");

            this.requestUri = config.RequestBaseUri().ToString();

            client = Utility.CreateHttp3Client(config.MaxConnectionsPerServer());

            string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
            authKeyHashFunction = new StringHMACSHA256Hash(authKey);
        }

        public async Task ExecuteOnceAsync()
        {
            string targetUri = this.requestUri;
            using (HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, targetUri))
            {
                // ref: https://github.com/dotnet/runtime/issues/987
                httpRequest.Version = new Version(3, 0);
                httpRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

                string dateHeaderValue = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
                httpRequest.Headers.Add(Microsoft.Azure.Documents.HttpConstants.HttpHeaders.XDate, dateHeaderValue);

                string authorization = AuthorizationHelper.GenerateKeyAuthorizationCore("GET",
                    dateHeaderValue,
                    "docs",
                    httpRequest.RequestUri.AbsolutePath.TrimStart(new char[] { '/' }),
                    authKeyHashFunction);
                httpRequest.Headers.TryAddWithoutValidation(Microsoft.Azure.Documents.HttpConstants.HttpHeaders.Authorization, authorization);

                using (HttpResponseMessage responseMessage = await client.SendAsync(httpRequest))
                {
                    responseMessage.EnsureSuccessStatusCode();

                    // Drain the response
                    using (Stream payload = await responseMessage.Content.ReadAsStreamAsync())
                    {
                        while (await payload.ReadAsync(buffer, 0, 4096) > 0) { }
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
