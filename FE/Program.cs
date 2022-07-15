// 1 input on HTTP port 8000 and maintain 4 connections for TCP routing per partition (assuming requests to the same partition)
// Backend/TCP servers should be started with ports 8009/8010/8011/8012

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Documents;
using System.IO.Pipelines;


var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((context, options) =>
{
    //options.ListenAnyIP(8000, listenOptions =>
    //{
    //    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    //    listenOptions.UseHttps();
    //});
});

var app = builder.Build();

app.Run(async context =>
{
    // Receives a request
    return;
});

app.Run();