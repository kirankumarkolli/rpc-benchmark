using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Documents;
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

            {
                // EntbdContext negotiation
                ReadResult readResult = await input.ReadAtLeastAsync(4);
                var buffer = readResult.Buffer;
                Debug.Assert(buffer.FirstSpan.Length >= 4);

                uint length = BitConverter.ToUInt32(readResult.Buffer.FirstSpan.Slice(0, sizeof(UInt32)));

                while (buffer.Length < length) // Read at-least length
                {
                    input.AdvanceTo(buffer.Start, readResult.Buffer.End);
                }

                // TODO: Incoming context validation 
                // 16 <- Activity id (hard coded)
                int connectionContextOffet = sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + 16;

                BytesDeserializer reader = new BytesDeserializer(buffer.Slice(connectionContextOffet, length-connectionContextOffet).ToArray(), (int) length - connectionContextOffet);
                RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
                request.ParseFrom(ref reader);


                // Send response 

                //RntbdConstants.ConnectionContextHeadersResponse responseHeaders = new RntbdConstants.ConnectionContextHeadersResponse();
                //responseHeaders.activityId.value.valueGuid = Guid.NewGuid();
                //responseHeaders.activityId.isPresent = true;

                //responseHeaders.statusCode.value.valueULong = 200;
                //responseHeaders.statusCode.isPresent = true;


                // TODO: Fill right values 
                RntbdConstants.ConnectionContextResponse contextResponse = new RntbdConstants.ConnectionContextResponse();

                contextResponse.protocolVersion.value.valueULong = RntbdConstants.CurrentProtocolVersion;
                contextResponse.protocolVersion.isPresent = true;

                contextResponse.serverVersion.value.valueBytes = HttpConstants.Versions.CurrentVersionUTF8;
                contextResponse.serverVersion.isPresent = true;

                contextResponse.clientVersion.value.valueBytes = HttpConstants.Versions.CurrentVersionUTF8;
                contextResponse.clientVersion.isPresent = true;

                contextResponse.serverAgent.value.valueBytes = Encoding.UTF8.GetBytes("RntbdServer");
                contextResponse.serverAgent.isPresent = true;

                byte[] responseMessage = contextResponse.Serialize(200, Guid.NewGuid());
                await connection.Transport.Output.WriteAsync(new Memory<byte>(responseMessage));
            }


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
