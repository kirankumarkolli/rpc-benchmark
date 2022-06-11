// This shows how a framework would implement a custom connection handler 

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Security.Authentication;
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
        public static IServiceCollection AddFramework(this IServiceCollection services, IPEndPoint endPoint)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, Rntbd2OptionsSetup>());

            services.Configure<Rntbd2OptionsSetup.Rntbd2Options>(o =>
            {
                o.EndPoint = endPoint;
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
            options.ListenLocalhost(_options.EndPoint.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.None;
                listenOptions.UseConnectionHandler<Rntbd2ConnectionHandler>();
                listenOptions.UseHttps();
                listenOptions.UseConnectionLogging();

                listenOptions.Use((context, next) =>
                {
                    var tlsFeature = context.Features.Get<ITlsHandshakeFeature>()!;

                    if(tlsFeature.Protocol == SslProtocols.Tls12 | tlsFeature.Protocol == SslProtocols.Tls13)
                    {
                        throw new NotSupportedException(
                            $"Prohibited Protocol: {tlsFeature.Protocol}");
                    }

                    if (tlsFeature.CipherAlgorithm == CipherAlgorithmType.Null)
                    {
                        throw new NotSupportedException(
                            $"Prohibited cipher: {tlsFeature.CipherAlgorithm}");
                    }

                    return next();
                });
            });
        }

        // The framework exposes options for how to bind
        public class Rntbd2Options
        {
            public IPEndPoint EndPoint { get; set; }
        }
    }
}
