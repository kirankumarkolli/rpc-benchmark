// This shows how a framework would implement a custom connection handler 

namespace KestrelTcpDemo
{
    using System;
    using System.Buffers;
    using System.Diagnostics;
    using System.IO;
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

    public static class SlbExtensions
    {
        public static IServiceCollection AddSlbFramework(this IServiceCollection services, IPEndPoint endPoint)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, SlbOptionsSetup>());

            services.Configure<SlbOptionsSetup.SlbOptions>(o =>
            {
                o.EndPoint = endPoint;
            });

            return services;
        }
    }

    public class SlbOptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private readonly SlbOptions _options;

        public SlbOptionsSetup(IOptions<SlbOptions> options)
        {
            _options = options.Value;
        }

        public void Configure(KestrelServerOptions options)
        {
            options.ListenAnyIP(_options.EndPoint.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.None;
                listenOptions.UseConnectionHandler<SlbConnectionHandler>();
                listenOptions.UseConnectionLogging();
            });
        }

        // The framework exposes options for how to bind
        public class SlbOptions
        {
            public IPEndPoint EndPoint { get; set; }
        }

        public class SlbConnectionHandler : ConnectionHandler
        {
            public override async Task OnConnectedAsync(ConnectionContext context)
            {
                await using (context)
                {

                    MemoryPool<byte> pool = context.Features.Get<IMemoryPoolFeature>()?.MemoryPool ?? MemoryPool<byte>.Shared;
                    Stream inputStream = context.Transport.Input.AsStream();
                    Stream outputStream = context.Transport.Output.AsStream();

                    using IMemoryOwner<byte> owner = pool.Rent();
                    
                    int bytesRead = 0;
                    while (true)
                    {
                        bytesRead = await inputStream.ReadAsync(owner.Memory);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        await outputStream.WriteAsync(owner.Memory.Slice(0, bytesRead));
                    }
                }
            }
        }
    }
}
