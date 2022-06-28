//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Documents.Collections;
    using Microsoft.Azure.Documents.Rntbd;

    internal class TcpServerBenchmarkOperation : IBenchmarkOperation
    {
        private static readonly byte[] buffer = new byte[4096];
        private readonly TransportClient tcpTransportClient;
        private readonly Uri requestUri;

        static readonly string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        private readonly string resourceId;
        private readonly IComputeHash authKeyHashFunction;
        private readonly string physicalAddress;

        public TcpServerBenchmarkOperation(BenchmarkConfig config)
        {
            // TODO: Plug the ItemTemplateFile E2E with server as well
            tcpTransportClient = Utility.CreateTcpClient(config.MaxConnectionsPerServer());

            authKeyHashFunction = new StringHMACSHA256Hash(authKey);
            this.resourceId = $"dbs/{config.Database}/colls/{config.Container}/docs/item1";
            this.requestUri = config.RequestBaseUri();
            this.physicalAddress = $"{config.EndPoint.Trim(new char[] { '/' })}/application/{Guid.NewGuid().ToString()}/partition/{Guid.NewGuid().ToString()}/replica/{Guid.NewGuid().ToString()}/";
        }

        public async Task ExecuteOnceAsync()
        {
            Microsoft.Azure.Documents.DocumentServiceRequest reqeust = Microsoft.Azure.Documents.DocumentServiceRequest.CreateFromName(
                    Microsoft.Azure.Documents.OperationType.Read,
                    resourceFullName: resourceId, // ResourceId
                    Microsoft.Azure.Documents.ResourceType.Document,
                    Microsoft.Azure.Documents.AuthorizationTokenType.PrimaryMasterKey);

            string dateHeaderValue = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
            reqeust.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.XDate] = dateHeaderValue;

            string authorization = AuthorizationHelper.GenerateKeyAuthorizationCore("GET", dateHeaderValue, "docs", resourceId, authKeyHashFunction);
            reqeust.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.Authorization] = authorization;

            using (ActivityScope activityScope = new ActivityScope(Guid.NewGuid()))
            {
                Microsoft.Azure.Documents.StoreResponse storeResponse = await tcpTransportClient.InvokeStoreAsync(
                    physicalAddress: new Uri(this.physicalAddress),
                    resourceOperation: Microsoft.Azure.Documents.ResourceOperation.ReadDocument,
                    request: reqeust);

                if (storeResponse.StatusCode != System.Net.HttpStatusCode.OK || storeResponse.ResponseBody == null)
                {
                    throw new Exception($"Unexpected status code {storeResponse.StatusCode}");
                }

                // drain response
                using (Stream payload = storeResponse.ResponseBody)
                {
                    while (await payload.ReadAsync(buffer, 0, 4096) > 0) { }
                }
            }
        }

        public Task PrepareAsync()
        {
            return Task.CompletedTask;
        }
    }
}
