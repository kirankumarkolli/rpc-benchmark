// This shows how a framework would implement a custom connection handler 

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace KestrelTcpDemo
{
    public static class RntbdExtensions
    {
        public static IServiceCollection AddFramework(this IServiceCollection services, IPEndPoint endPoint, String sslCertSubject)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, Rntbd2OptionsSetup>());

            services.Configure<Rntbd2OptionsSetup.Rntbd2Options>(o =>
            {
                o.EndPoint = endPoint;
                o.SslCertSubject = sslCertSubject;
            });

            return services;
        }
    }

    public class Rntbd2OptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private readonly Rntbd2Options _options;

        public Rntbd2OptionsSetup(IOptions<Rntbd2Options> options)
        {
            _options = options.Value;
        }

        public void Configure(KestrelServerOptions options)
        {
            options.ListenAnyIP(_options.EndPoint.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.None;
                listenOptions.UseConnectionHandler<InMemoryRntbd2ConnectionHandler>();
                if (!String.IsNullOrWhiteSpace(_options.SslCertSubject))
                {
                    listenOptions.UseHttps(
                        StoreName.My,
                        _options.SslCertSubject,
                        true,
                        StoreLocation.LocalMachine);
                }
                else
                {
                    listenOptions.UseHttps();
                }
                listenOptions.UseConnectionLogging();
            });
        }

        // The framework exposes options for how to bind
        public class Rntbd2Options
        {
            public IPEndPoint EndPoint { get; set; }

            public string SslCertSubject { get; set; }
        }
    }
}
