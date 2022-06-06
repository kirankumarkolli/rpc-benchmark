using System;
using System.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace KestrelTcpDemo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                    {
                        // This shows how a custom framework could plug in an experience without using Kestrel APIs directly
                        services.AddFramework(new IPEndPoint(IPAddress.Loopback, 8009));
                    })
                .UseKestrel(options =>
                    {
                        //// TCP 8007
                        //options.ListenLocalhost(8007, builder =>
                        //{
                        //    builder.UseConnectionHandler<MyEchoConnectionHandler>();
                        //});

                        //// HTTP 5000
                        //options.ListenLocalhost(5000);

                        //// HTTPS 5001
                        //options.ListenLocalhost(5001, builder =>
                        //{
                        //    builder.UseHttps();
                        //});

                        options.ListenLocalhost(7070,
                            listenOptions => listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1
                            );

                        options.ListenLocalhost(8080, 
                            listenOptions => listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2
                            );

                        options.ListenLocalhost(9090,
                            listenOptions => 
                                { 
                                    listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3;
                                    listenOptions.UseHttps();
                                }
                            );
                    })
                .ConfigureLogging((context, loggingBuilder) => 
                    {
                        loggingBuilder.ClearProviders();
                    })
                .UseStartup<Startup>();
    }
}
