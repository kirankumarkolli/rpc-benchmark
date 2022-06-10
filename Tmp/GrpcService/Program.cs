namespace GrpcService
{
    using System;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Server.Kestrel.Core;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.Hosting;

    public class Program
    {
        public static void Main(string[] args)
        {
            ServiceConfig config = ServiceConfig.From(args);
            config.Print();

            switch (config.WorkloadType.ToLowerInvariant())
            {
                case "grpc":
                    Console.WriteLine("Starting gRPC server...");
                    CreateHostBuilder(args).Build().Run();
                    break;
                case "http2":
                    Console.WriteLine("Starting http2 server...");
                    WebHost.CreateDefaultBuilder()
                        .ConfigureKestrel(options =>
                        {
                            options.ListenAnyIP(8091, listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http2;
                                listenOptions.UseHttps();
                                listenOptions.KestrelServerOptions.Limits.Http2.MaxStreamsPerConnection = 2 * Environment.ProcessorCount;

                            });
                        })
                        .UseStartup<Http2Startup>()
                        .Build()
                        .Run();
                    break;
                //case "http3":
                //    Console.WriteLine("Dotnet HTTP3 is not yet ready");
                //    Http3Server();
                //    break;
                default:
                    throw new NotSupportedException();
            }
        }

        public static void Http3Server()
        {
            var cert = CertificateLoader.LoadFromStoreCert("localhost", StoreName.My.ToString(), StoreLocation.CurrentUser, true);

            var hostBuilder = new HostBuilder()
                .ConfigureLogging((_, factory) =>
                {
                    //factory.SetMinimumLevel(LogLevel.Trace);
                    //factory.AddConsole();
                })
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel()
                    .UseQuic(options =>
                    {
                        options.Certificate = cert; // Shouldn't need this either here.
                        options.Alpn = "h3-29"; // Shouldn't need to populate this as well.
                        options.IdleTimeout = TimeSpan.FromHours(1);
                    })
                    .ConfigureKestrel((context, options) =>
                    {
                        var basePort = 5557;
                        options.EnableAltSvc = true;

                        options.Listen(IPAddress.Any, basePort, listenOptions =>
                        {
                            listenOptions.UseHttps(httpsOptions =>
                            {
                                httpsOptions.ServerCertificate = cert;
                            });
                            listenOptions.Protocols = HttpProtocols.Http3;
                        });
                    })
                    .UseStartup<Startup>();
                });

            hostBuilder.Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        private class Http2Startup
        {

            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
            {
                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }

                app.UseRouting();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/dbs/{name}", async context =>
                    {
                        context.Response.ContentType = "application/json";
                        context.Response.StatusCode = 200;

                        IHeaderDictionary responseHeaders = context.Response.Headers;
                        responseHeaders["x-ms-activity-id"] = Guid.NewGuid().ToString();
                        responseHeaders["x-ms-request-charge"] = "1.0";
                        responseHeaders["x-ms-session-token"] = "9898";

                        await context.Response.WriteAsync(GreeterService.testJsonPayload);
                    });
                });
            }
        }

        public class Http3Startup
        {
            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app)
            {
                app.Run(async context =>
                {
                    var memory = new Memory<byte>(new byte[4096]);
                    var length = await context.Request.Body.ReadAsync(memory);
                    context.Response.Headers["test"] = "foo";
                    // for testing
                    await context.Response.WriteAsync("Hello World! " + context.Request.Protocol);
                });
            }
        }
    }
}
