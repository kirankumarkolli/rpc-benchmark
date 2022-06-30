using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Primitives;
using System.Globalization;

namespace FE
{
    internal static class RequestHelper
    {
        public static bool TryClassifyRequest(HttpRequest request, out OperationType operationType)
        {
            operationType = OperationType.None;
            
            if (request.Method == HttpMethod.Get.Method)
            {
                operationType = OperationType.Read;
                return true;
            }

            return false;
        }

        public static bool TryGetRoutingInformation(HttpRequest request, out RequestRoutingInformation requestRoutingInformation)
        {
            requestRoutingInformation = default;

            if (!request.Path.HasValue)
            {
                return false;
            }

            ReadOnlySpan<char> path = request.Path.Value.AsSpan();

            int currentIndex = 0;
            int nextIndex = 0;
            ReadOnlySpan<char> value = ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> database = ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> container = ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> documentId = ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> partitionKeyValue = ReadOnlySpan<char>.Empty;
            // /dbs/{db}/colls/{container}/docs/{id}

            while ((nextIndex = path.Slice(currentIndex).IndexOf('/')) > -1)
            {
                value = path.Slice(currentIndex, nextIndex);
                currentIndex += nextIndex + 1;
                if (string.Empty.AsSpan().CompareTo(value, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    continue;
                }

                if (database.IsEmpty)
                {
                    if ("dbs".AsSpan().CompareTo(value, StringComparison.InvariantCultureIgnoreCase) != 0)
                    {
                        database = value;
                    }
                }
                else if (container.IsEmpty)
                {
                    if ("colls".AsSpan().CompareTo(value, StringComparison.InvariantCultureIgnoreCase) != 0)
                    {
                        container = value;
                    }
                }
            }

            documentId = path.Slice(currentIndex);

            if (request.Headers.TryGetValue(Microsoft.Azure.Documents.HttpConstants.HttpHeaders.PartitionKey, out StringValues partitionKey))
            {
                partitionKeyValue = partitionKey[0].AsSpan();
            }

            requestRoutingInformation = new RequestRoutingInformation(
                    database,
                    container,
                    documentId,
                    partitionKeyValue
                );

            return true;
        }

        public static TransportClient CreateTcpClient(int maxConnectionsPerServer)
        {
            return new Microsoft.Azure.Documents.Rntbd.TransportClient(
                new Microsoft.Azure.Documents.Rntbd.TransportClient.Options(TimeSpan.FromSeconds(240))
                {
                    MaxChannels = maxConnectionsPerServer,
                    PartitionCount = 8,
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
        }

        public static bool TryRouteToEndpoint(HttpRequest request, out (Uri, DocumentServiceRequest) routing)
        {
            routing = default;
            RequestRoutingInformation requestRoutingInformation;
            if (!RequestHelper.TryGetRoutingInformation(request, out requestRoutingInformation))
            {
                return false;
            }

            // TODO: Implement routing logic
            string resourceId = request.Path.Value;
            Microsoft.Azure.Documents.DocumentServiceRequest dsr = Microsoft.Azure.Documents.DocumentServiceRequest.CreateFromName(
                    Microsoft.Azure.Documents.OperationType.Read,
                    resourceFullName: resourceId, // ResourceId
                    Microsoft.Azure.Documents.ResourceType.Document,
                    Microsoft.Azure.Documents.AuthorizationTokenType.PrimaryMasterKey);

            dsr.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.XDate] = request.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.XDate];

            dsr.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.Authorization] = request.Headers[Microsoft.Azure.Documents.HttpConstants.HttpHeaders.Authorization];

            // TODO: Routing to a single endpoint for testing purposes
            routing = (
                new Uri("https://localhost:8001"),
                dsr);

            return true;
        }
    }
}
