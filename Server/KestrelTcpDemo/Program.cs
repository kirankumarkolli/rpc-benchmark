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
            //AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            //AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);

            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                    {
                        // This shows how a custom framework could plug in an experience without using Kestrel APIs directly
                        if (args.Length == 0 || !int.TryParse(args[0], out int port))
                        {
                            throw new ArgumentException("Run it with a port: dotnet run <port>");
                        }
                        
                        services.AddFramework(new IPEndPoint(IPAddress.Loopback, port));
                    })
                //.UseKestrel(options =>
                //    {
                //        //options.ListenLocalhost(7070, listenOptions =>
                //        //    {
                //        //        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
                //        //        listenOptions.UseHttps();
                //        //    });

                //        //options.ListenLocalhost(8080, listenOptions =>
                //        //    {
                //        //        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
                //        //        listenOptions.UseHttps();
                //        //    });

                //        // HTTP3
                //        //options.ListenLocalhost(9090,
                //        //    listenOptions => 
                //        //        { 
                //        //            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3;
                //        //            listenOptions.UseHttps();
                //        //        }
                //        //    );
                //    })
                .ConfigureLogging((context, loggingBuilder) => 
                    {
                        loggingBuilder.ClearProviders();
                    })
                .UseStartup<Startup>();
    }
}
