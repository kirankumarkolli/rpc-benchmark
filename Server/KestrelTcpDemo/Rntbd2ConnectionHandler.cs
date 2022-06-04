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
    // This is the connection handler the framework uses to handle new incoming connections
    internal class Rntbd2ConnectionHandler : ConnectionHandler
    {
        private readonly IRntbdMessageParser _parser;

        public Rntbd2ConnectionHandler(IRntbdMessageParser parser)
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

                while (buffer.Length < length) // Read at-least message
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
            RntbdMessage message)
        {
            foreach (var segment in buffer)
            {
                await connection.Transport.Output.WriteAsync(segment);
            }
        }
    }
}
