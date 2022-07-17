﻿using CosmosBenchmark;
using Kestrel.Clone;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Azure.Cosmos.Rntbd;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Documents;
using static Microsoft.Azure.Documents.RntbdConstants;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace KestrelTcpDemo
{
    internal abstract class BaseRntbd2ConnectionHandler : ConnectionHandler
    {
        private readonly Func<Stream, SslStream> _sslStreamFactory;
        private static ConcurrentDictionary<string, X509Certificate2> cachedCerts = new ConcurrentDictionary<string, X509Certificate2>();

        public BaseRntbd2ConnectionHandler()
        {
            _sslStreamFactory = s => new SslStream(s, leaveInnerStreamOpen: true, userCertificateValidationCallback: null);
        }

        private SslDuplexPipe CreateSslDuplexPipe(IDuplexPipe transport, MemoryPool<byte> memoryPool)
        {
            StreamPipeReaderOptions inputPipeOptions = new StreamPipeReaderOptions
            (
                pool: memoryPool,
                //bufferSize: memoryPool.GetMinimumSegmentSize(),
                //minimumReadSize: memoryPool.GetMinimumAllocSize(),
                leaveOpen: true,
                useZeroByteReads: true
            );

            var outputPipeOptions = new StreamPipeWriterOptions
            (
                pool: memoryPool,
                leaveOpen: true
            );

            return new SslDuplexPipe(transport, inputPipeOptions, outputPipeOptions, _sslStreamFactory);
        }

        internal static X509Certificate2 GetServerCertificate(string serverName)
        {
            X509Store store = new X509Store("MY", StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);


            foreach (X509Certificate2 x509 in store.Certificates)
            {
                if (x509.HasPrivateKey)
                {
                    // TODO: Covering for "CN="
                    if (x509.SubjectName.Name.EndsWith(serverName))
                    {
                        return x509;
                    }
                }
            }

            throw new Exception("GetServerCertificate didn't find any");
        }

        private async Task DoOptionsBasedHandshakeAsync(ConnectionContext context, SslStream sslStream, CancellationToken cancellationToken)
        {
            //var serverCert = GetServerCertificate("backend-fake");
            var sslOptions = new SslServerAuthenticationOptions
            {
                //ServerCertificate = serverCert,
                ServerCertificateSelectionCallback = (object sender, string? hostName) =>
                {
                    if (cachedCerts.TryGetValue(hostName, out X509Certificate2 cachedCert))
                    {
                        return cachedCert;
                    }


                    X509Certificate2 newCert = GetServerCertificate(hostName);
                    cachedCerts.TryAdd(hostName, newCert);
                    return newCert;
                },
                //ServerCertificateContext = SslStreamCertificateContext.Create(serverCert, additionalCertificates: null),
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
        }

        public override async Task OnConnectedAsync(ConnectionContext context)
        {
            await using (context)
            {
                var sslDuplexPipe = CreateSslDuplexPipe(
                    context.Transport,
                    context.Features.Get<IMemoryPoolFeature>()?.MemoryPool ?? MemoryPool<byte>.Shared);
                var sslStream = sslDuplexPipe.Stream;

                // Server SSL auth
                await DoOptionsBasedHandshakeAsync(context, sslStream, CancellationToken.None);
                using (CosmosDuplexPipe cosmosDuplexPipe = new CosmosDuplexPipe(sslStream))
                {
                    // Complete Rntbd context negotiation 
                    await cosmosDuplexPipe.NegotiateRntbdContextAsServer();

                    // Process RntbdMessages
                    await ProcessRntbdFlowsAsync(context.ConnectionId, cosmosDuplexPipe);
                }
            }
        }

        public async Task ProcessRntbdFlowsAsync(string connectionId, CosmosDuplexPipe cosmosDuplexPipe)
        {
            try
            {
                await ProcessRntbdFlowsAsyncCore(connectionId, cosmosDuplexPipe);
            }
            catch (ConnectionResetException ex)
            {
                Trace.TraceInformation(ex.ToString());
            }
            catch (InvalidOperationException ex) // Connection reset dring Read/Write
            {
                Trace.TraceError(ex.ToString());
            }
            finally
            {
                Trace.TraceWarning($"Connection {connectionId} completed");

                // ConnectionContext.DisposeAsync() should take care of below
                //await connection.Transport.Input.CompleteAsync();
                //await connection.Transport.Output.CompleteAsync();
            }
        }

        public abstract Task ProcessRntbdFlowsAsyncCore(string connectionId, CosmosDuplexPipe cosmosDuplexPipe);
    }
}
