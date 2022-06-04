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
using static Microsoft.Azure.Documents.RntbdConstants;

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
            await NegotiateRntbdContext(connection);

            // Code to parse length prefixed encoding
            while (true)
            {
                (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(connection);
                var buffer = readResult.Buffer;

                if (length != -1) // request already completed
                {
                    await ProcessMessageAsync(connection, buffer);
                }

            }
        }

        private static async ValueTask<(int, ReadResult)> ReadLengthPrefixedMessageFull(ConnectionContext connection)
        {
            var input = connection.Transport.Input;
            int length = -1;

            ReadResult readResult = await input.ReadAtLeastAsync(4);
            var buffer = readResult.Buffer;
            if (!readResult.IsCompleted)
            {
                Debug.Assert(buffer.FirstSpan.Length >= 4);

                length = (int)BitConverter.ToUInt32(readResult.Buffer.FirstSpan.Slice(0, sizeof(UInt32)));

                if (buffer.Length < length) // Read at-least length
                {
                    input.AdvanceTo(buffer.Start, readResult.Buffer.End);
                    readResult = await input.ReadAtLeastAsync(length);
                }
            }

            return (length, readResult);
        }

        private static async Task NegotiateRntbdContext(ConnectionContext connection)
        {
            // RntbdContext negotiation
            (int length, ReadResult readResult) = await ReadLengthPrefixedMessageFull(connection);
            var buffer = readResult.Buffer;

            // TODO: Incoming context validation 
            // 16 <- Activity id (hard coded)
            int connectionContextOffet = sizeof(UInt32) + sizeof(UInt16) + sizeof(UInt16) + 16;

            BytesDeserializer reader = new BytesDeserializer(buffer.Slice(connectionContextOffet, length - connectionContextOffet).ToArray(), length - connectionContextOffet);
            RntbdConstants.ConnectionContextRequest request = new RntbdConstants.ConnectionContextRequest();
            request.ParseFrom(ref reader);

            // Mark the incoming message as consumed
            // TODO: Test exception scenarios (i.e. if processing alter fails its impact)
            connection.Transport.Input.AdvanceTo(buffer.GetPosition(length), readResult.Buffer.End);

            // Send response 
            byte[] responseMessage = RntbdConstants.ConnectionContextResponse.Serialize(200, Guid.NewGuid());
            await connection.Transport.Output.WriteAsync(new Memory<byte>(responseMessage));
        }

        private async Task ProcessMessageAsync(
            ConnectionContext connection,
            ReadOnlySequence<byte> buffer)
        {
            BytesDeserializer reader = new BytesDeserializer(buffer.Slice(4, buffer.Length - 4).ToArray(), (int)buffer.Length - 4);
            Request request = new Request();
            request.ParseFrom(ref reader);

            foreach (var segment in buffer)
            {
                await connection.Transport.Output.WriteAsync(segment);
            }
        }
    }
}
