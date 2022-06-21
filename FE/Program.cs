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
    var req = context.Request;
    await context.Response.WriteAsync("Hello from 2nd delegate.");
});

app.Run();


enum OperationType
{
    Create,
    Read
}