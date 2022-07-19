
namespace KestrelTcpDemo
{
    using System;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Connections;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;

    public class Program
    {
        public static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);

            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                    {
                        string thinClientPortString = Environment.GetEnvironmentVariable("ThinClientPort");
                        int thinClientPort = 8009;
                        if (!string.IsNullOrEmpty(thinClientPortString))
                        {
                            thinClientPort = int.Parse(thinClientPortString);
                        }

                        int? slbPort = null;
                        string slbPortString = Environment.GetEnvironmentVariable("SlbPort");
                        if (!string.IsNullOrEmpty(slbPortString))
                        {
                            slbPort = int.Parse(slbPortString);
                        }

                        if (slbPort != null)
                        {
                            services.AddSlbFramework(new IPEndPoint(IPAddress.Any, slbPort.Value));
                        }

                        // This shows how a custom framework could plug in an experience without using Kestrel APIs directly
                        if (args != null && args.Length > 0)
                        {
                            services.AddFramework(new IPEndPoint(IPAddress.Any, thinClientPort), args[0]);
                        }
                        else
                        {
                            string sslCertificateSubjectName = Environment.GetEnvironmentVariable("RuntimeSslCertificateSubjectName");
                            services.AddFramework(new IPEndPoint(IPAddress.Any, thinClientPort), sslCertificateSubjectName);
                        }
                    })
                .UseKestrel(options =>
                    {
                        //options.ListenAnyIP(7070, listenOptions =>
                        //    {
                        //        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;

                        //        if (args != null && args.Length > 0)
                        //        {
                        //            listenOptions.UseHttps(StoreName.My, args[0], true, StoreLocation.LocalMachine);
                        //        }
                        //        else
                        //        {
                        //            listenOptions.UseHttps();
                        //        }
                        //    });

                        //options.ListenAnyIP(8080, listenOptions =>
                        //    {
                        //        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
                        //        if (args != null && args.Length > 0)
                        //        {
                        //            listenOptions.UseHttps(StoreName.My, args[0], true, StoreLocation.LocalMachine);
                        //        }
                        //        else
                        //        {
                        //            listenOptions.UseHttps();
                        //        }
                        //    });

                        // HTTP3
                        //options.ListenLocalhost(9090,
                        //    listenOptions => 
                        //        { 
                        //            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3;
                        //            listenOptions.UseHttps();
                        //        }
                        //    );
                    })
                .ConfigureLogging((context, loggingBuilder) => 
                    {
                        loggingBuilder.ClearProviders();
                    })
                .UseStartup<Startup>();
    }
}
