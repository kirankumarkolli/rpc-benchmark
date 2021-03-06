﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Documents.Collections;
    using Microsoft.Azure.Documents.Rntbd;

    internal class TcpServerBenchmarkOperation : IBenchmarkOperation
    {
        static TransportClient tcpTransportClient;
        private readonly Uri requestUri;

        public TcpServerBenchmarkOperation(BenchmarkConfig config)
        {
            if (TcpServerBenchmarkOperation.tcpTransportClient == null)
            {
                TcpServerBenchmarkOperation.tcpTransportClient = Utility.CreateTcpClient(config.MaxConnectionsPerServer());
            }

            this.requestUri = config.RequestBaseUri();
        }

        public async Task ExecuteOnceAsync()
        {
            Microsoft.Azure.Documents.DocumentServiceRequest reqeust = Microsoft.Azure.Documents.DocumentServiceRequest.CreateFromName(
                    Microsoft.Azure.Documents.OperationType.Read,
                    "dbs/db1/col/col1/doc/item1", // ResourceId
                    Microsoft.Azure.Documents.ResourceType.Document,
                    Microsoft.Azure.Documents.AuthorizationTokenType.PrimaryMasterKey);

            using (ActivityScope activityScope = new ActivityScope(Guid.NewGuid()))
            {
                Microsoft.Azure.Documents.StoreResponse storeResponse = await TcpServerBenchmarkOperation.tcpTransportClient.InvokeStoreAsync(
                    //physicalAddress: new Uri("rnbd://cdb-ms-prod-eastus1-fd40.documents.azure.com:14364"),
                    physicalAddress: this.requestUri,
                    resourceOperation: Microsoft.Azure.Documents.ResourceOperation.ReadDocument,
                    request: reqeust);

                Microsoft.Azure.Documents.DocumentServiceResponse documentServiceResponse = this.CompleteResponse(storeResponse, reqeust);

                if (documentServiceResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Unexpected status code {storeResponse.StatusCode}");
                }
            }
        }
        private Microsoft.Azure.Documents.DocumentServiceResponse CompleteResponse(
            Microsoft.Azure.Documents.StoreResponse storeResponse,
            Microsoft.Azure.Documents.DocumentServiceRequest request)
        {
            INameValueCollection headersFromStoreResponse = storeResponse.Headers;

            Microsoft.Azure.Documents.DocumentServiceResponse response = new Microsoft.Azure.Documents.DocumentServiceResponse(
                storeResponse.ResponseBody,
                headersFromStoreResponse,
                (HttpStatusCode)storeResponse.Status,
                null,
                request.SerializerSettings);

            return response;
        }

        public Task PrepareAsync()
        {
            return Task.CompletedTask;
        }
    }
}
