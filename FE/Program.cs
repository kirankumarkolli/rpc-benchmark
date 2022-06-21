// 1 input on HTTP port 8000 and maintain 4 connections for TCP routing per partition (assuming requests to the same partition)

using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.ListenAnyIP(8000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        listenOptions.UseHttps();
    });
});

var app = builder.Build();

app.Run(async context =>
{
    // Obtain the hash of the request to know which partition to route to
    
    // Obtain the type of request

    // Route to one of the established connections and pipe the response
    var req = context.Request;
    await context.Response.WriteAsync("Hello from 2nd delegate.");
});

app.Run();


// Write goes to primary, Read to any
enum OperationType
{
    Create,
    Read
}



