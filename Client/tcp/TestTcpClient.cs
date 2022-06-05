using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

            using (transportClient)
            {
                string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
                IComputeHash authKeyHashFunction = new StringHMACSHA256Hash(authKey);

                string resourceId = "dbs/db1/colls/col1/docs/item1";
                Documents.DocumentServiceRequest reqeust = Documents.DocumentServiceRequest.CreateFromName(
                        Documents.OperationType.Read,
                        resourceFullName: resourceId, // ResourceId
                        Documents.ResourceType.Document,
                        Documents.AuthorizationTokenType.PrimaryMasterKey);

                string dateHeaderValue = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
                reqeust.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.XDate] = dateHeaderValue;

                string authorization = AuthorizationHelper.GenerateKeyAuthorizationCore("GET", dateHeaderValue, "docs", resourceId, authKeyHashFunction);
                reqeust.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.Authorization] = authorization;

                for (int i = 0; i < 1000; i++)
                {
                    using (ActivityScope activityScope = new ActivityScope(Guid.NewGuid()))
                    {
                        Microsoft.Azure.Documents.StoreResponse storeResponse = await transportClient.InvokeStoreAsync(
                            //physicalAddress: new Uri("rnbd://cdb-ms-prod-eastus1-fd40.documents.azure.com:14364"),
                            physicalAddress: new Uri("http://127.0.0.1:8009/application/0749FECF-F6B0-41DC-8DB4-A19E214B1B0B/partition/4FCA2450-6D61-46A5-B971-0B1903204338/replica/6753AFE4-C375-4284-B70C-51910C16C902/"),
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
    }
}
