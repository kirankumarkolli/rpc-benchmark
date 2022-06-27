// 1 input on HTTP port 8000 and maintain 4 connections for TCP routing per partition (assuming requests to the same partition)
// Backend/TCP servers should be started with ports 8009/8010/8011/8012

using FE;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Documents;
using System.IO.Pipelines;

string authKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
int maxConnectionsPerServer;
if (!int.TryParse(Environment.GetCommandLineArgs()[1], out maxConnectionsPerServer){
    maxConnectionsPerServer = 30;
}

TransportClient tcpTransportClient = RequestHelper.CreateTcpClient(maxConnectionsPerServer);


var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.ListenAnyIP(8000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        listenOptions.UseHttps();
    });
});

builder.Services.AddSingleton<IComputeHash>((sp) => new StringHMACSHA256Hash(authKey));

var app = builder.Build();

app.Run(async context =>
{
    // Obtain the type of request
    if (!RequestHelper.TryClassifyRequest(context.Request, out OperationType operationType)
        || operationType == OperationType.None)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync($"{DateTime.UtcNow} : Cannot classify request.");
        return;
    }

    if (operationType == OperationType.Write)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync($"{DateTime.UtcNow} : Not supporting Write requests atm.");
        return;
    }

    if (!RequestHelper.TryRouteToEndpoint(context.Request, out (Uri, DocumentServiceRequest) routingInformation))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync($"{DateTime.UtcNow} : Could not build request routing information");
        return;
    }

    using (ActivityScope activityScope = new ActivityScope(Guid.NewGuid()))
    {
        Microsoft.Azure.Documents.StoreResponse storeResponse = await tcpTransportClient.InvokeStoreAsync(
            physicalAddress: routingInformation.Item1,
            resourceOperation: Microsoft.Azure.Documents.ResourceOperation.ReadDocument,
            request: routingInformation.Item2);

        if (storeResponse.StatusCode != System.Net.HttpStatusCode.OK || storeResponse.ResponseBody == null)
        {
            throw new Exception($"Unexpected status code {storeResponse.StatusCode}");
        }

        // drain response
        using (storeResponse.ResponseBody)
        {
            context.Response.StatusCode = (int)storeResponse.StatusCode;
            context.Response.Headers.ContentLength = storeResponse.ResponseBody.Length;
            context.Response.Headers.Add(HttpConstants.HttpHeaders.RequestCharge, "1.0");

            PipeWriter pipeWriter = context.Response.BodyWriter;
            PipeReader pipeReader = PipeReader.Create(storeResponse.ResponseBody);
            await pipeReader.CopyToAsync(pipeWriter);
        }
    }
});

app.Run();


// Write goes to primary, Read to any
enum OperationType
{
    None,
    Write,
    Read
}

readonly ref struct RequestRoutingInformation
{
    public RequestRoutingInformation(
        ReadOnlySpan<char> database,
        ReadOnlySpan<char> container,
        ReadOnlySpan<char> documentId,
        ReadOnlySpan<char> partitionKeyValue)
    {
        this.Database = database;
        this.Container = container;
        this.DocumentId = documentId;
        this.PartitionKeyValue = partitionKeyValue;
    }

    public ReadOnlySpan<char> Database { get; init; }
    public ReadOnlySpan<char> Container { get; init; }
    public ReadOnlySpan<char> DocumentId { get; init; }

    public ReadOnlySpan<char> PartitionKeyValue { get; init; }
}