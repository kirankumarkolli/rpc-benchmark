// This shows how a framework would implement a custom connection handler 

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace KestrelTcpDemo
{
    public static class MyFrameworkExtensions
    {
        public static IServiceCollection AddFramework(this IServiceCollection services, IPEndPoint endPoint)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, Rntbd2OptionsSetup>());

            services.Configure<MyFrameworkOptions>(o =>
            {
                o.EndPoint = endPoint;
            });

            services.TryAddSingleton<IFrameworkMessageParser, FrameworkMessageParser>();
            return services;
        }
    }

    public class Rntbd2OptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private readonly MyFrameworkOptions _options;

        public Rntbd2OptionsSetup(IOptions<MyFrameworkOptions> options)
        {
            _options = options.Value;
        }

        public void Configure(KestrelServerOptions options)
        {
            options.Listen(_options.EndPoint, builder =>
            {
                builder.UseConnectionHandler<Rntbd2ConnectionHandler>();
            });
        }

        // This is the connection handler the framework uses to handle new incoming connections
        private class Rntbd2ConnectionHandler : ConnectionHandler
        {
            private readonly IFrameworkMessageParser _parser;

            public Rntbd2ConnectionHandler(IFrameworkMessageParser parser)
            {
                _parser = parser;
            }

            public override async Task OnConnectedAsync(ConnectionContext connection)
            {
                var input = connection.Transport.Input;

                // Code to parse length prefixed encoding
                while (true)
                {
                    ReadResult readResult = await input.ReadAtLeastAsync(4);
                    var buffer = readResult.Buffer;
                    Debug.Assert(buffer.FirstSpan.Length >= 4);

                    uint length = BitConverter.ToUInt32(readResult.Buffer.FirstSpan.Slice(0, sizeof(UInt32)));

                    while(buffer.Length < length) // Read at-least message
                    {
                        input.AdvanceTo(buffer.Start, readResult.Buffer.End);
                    }


                    if (_parser.TryParseMessage(ref buffer, out var message))
                    {
                        await ProcessMessageAsync(connection, buffer, message);
                    }

                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }

            private async Task ProcessMessageAsync(
                ConnectionContext connection,
                ReadOnlySequence<byte> buffer, 
                Message message)
            {
                foreach (var segment in buffer)
                {
                    await connection.Transport.Output.WriteAsync(segment);
                }
            }
        }
    }

    // The framework exposes options for how to bind
    public class MyFrameworkOptions
    {
        public IPEndPoint EndPoint { get; set; }
    }

    // The framework exposes a message parser used to parse incoming protocol messages from the network
    public interface IFrameworkMessageParser
    {
        bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out Message message);
    }

    public class FrameworkMessageParser : IFrameworkMessageParser
    {
        public bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out Message message)
        {
            message = null;
            string content = Encoding.ASCII.GetString(buffer.ToArray(), 0, (int)buffer.Length);

            // Check for end-of-file tag. If it is not there, read
            // more data.  
            if (content.IndexOf("<EOF>") > -1)
            {
                message = new Message();
                return true;
            }

            return false;
        }
    }

    public class Message
    {
        // TODO: Add properties relevant to your message type here
    }
}
