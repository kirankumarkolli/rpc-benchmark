using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Rntbd;

namespace Microsoft.Azure.Cosmos
{
    public class TestTcpClient
    {
        public static async Task ReadTest()
        {
            Console.WriteLine("Hello world");
            TransportClient transportClient = new TransportClient(
                new TransportClient.Options(TimeSpan.FromSeconds(240))
                {
                    MaxChannels = 1,
                    PartitionCount = 1,
                    MaxRequestsPerChannel = 10,
                    //PortReuseMode = PortReuseMode.,
                    //PortPoolReuseThreshold = rntbdPortPoolReuseThreshold,
                    //PortPoolBindAttempts = rntbdPortPoolBindAttempts,
                    ReceiveHangDetectionTime = TimeSpan.FromSeconds(20),
                    SendHangDetectionTime = TimeSpan.FromSeconds(20),
                    //UserAgent = "TestClient",
                    //CertificateHostNameOverride = overrideHostNameInCertificate,
                    OpenTimeout = TimeSpan.FromSeconds(240),
                    TimerPoolResolution = TimeSpan.FromSeconds(2),
                    IdleTimeout = TimeSpan.FromSeconds(20),
                    EnableCpuMonitor = true,
                    // CallerId = callerId,
                    //ConnectionStateListener = this.connectionStateListener
                });

            Documents.DocumentServiceRequest reqeust = Documents.DocumentServiceRequest.CreateFromName(
                    Documents.OperationType.Read,
                    "dbs/db1/col/col1/doc/item1", // ResourceId
                    Documents.ResourceType.Document,
                    Documents.AuthorizationTokenType.PrimaryMasterKey);

            using (ActivityScope activityScope = new ActivityScope(Guid.NewGuid()))
            {
                Microsoft.Azure.Documents.StoreResponse storeResponse = await transportClient.InvokeStoreAsync(
                    //physicalAddress: new Uri("rnbd://cdb-ms-prod-eastus1-fd40.documents.azure.com:14364"),
                    physicalAddress: new Uri("https://127.0.0.1:8009"),
                    resourceOperation: Microsoft.Azure.Documents.ResourceOperation.ReadDocument,
                    request: reqeust);

                if (storeResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception($"Unexpected status code {storeResponse.StatusCode}");
                }
            }
        }
    }
}
