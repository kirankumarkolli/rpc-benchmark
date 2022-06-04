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

            services.TryAddSingleton<IRntbdMessageParser, RntbdMessageParser>();
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
            options.Listen(_options.EndPoint, builder =>
            {
                builder.UseConnectionHandler<Rntbd2ConnectionHandler>();
                //builder.UseHttps();
            });

            //options.ConfigureHttpsDefaults(httpsOptions =>
            //{
            //    httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            //});
        }

        // The framework exposes options for how to bind
        public class Rntbd2Options
        {
            public IPEndPoint EndPoint { get; set; }
        }
    }


    // The framework exposes a message parser used to parse incoming protocol messages from the network
    public interface IRntbdMessageParser
    {
        bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out RntbdMessage message);
    }

    public class RntbdMessageParser : IRntbdMessageParser
    {
        public bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out RntbdMessage message)
        {
            message = null;
            string content = Encoding.ASCII.GetString(buffer.ToArray(), 0, (int)buffer.Length);

            // Check for end-of-file tag. If it is not there, read
            // more data.  
            if (content.IndexOf("<EOF>") > -1)
            {
                message = new RntbdMessage();
                return true;
            }

            return false;
        }
    }

    public class RntbdMessage
    {
        // TODO: Add properties relevant to your message type here
    }
}
