//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Diagnostics;
    using System.Net.Sockets;
    using System.Threading;
    using Microsoft.Azure.Cosmos.Core.Trace;

    internal partial class CosmosDuplexPipe : IDisposable
    {
        internal static void SetCommonSocketOptions(Socket clientSocket)
        {
            clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            EnableTcpKeepAlive(clientSocket);
        }

        private static void EnableTcpKeepAlive(Socket clientSocket)
        {
            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

#if !NETSTANDARD15 && !NETSTANDARD16
            // This code should use RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            // but the feature is unavailable on .NET Framework 4.5.1.
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    clientSocket.IOControl(
                        IOControlCode.KeepAliveValues,
                        keepAliveConfiguration.Value,
                        null);
                }
                catch (Exception e)
                {
                    DefaultTrace.TraceWarning("IOControl(KeepAliveValues) failed: {0}", e);
                    // Ignore the exception.
                }
            }
#endif  // !NETSTANDARD15 && !NETSTANDARD16
        }

        private static readonly Lazy<byte[]> keepAliveConfiguration =
            new Lazy<byte[]>(GetWindowsKeepAliveConfiguration,
                LazyThreadSafetyMode.ExecutionAndPublication);

        private static byte[] GetWindowsKeepAliveConfiguration()
        {
            const uint EnableKeepAlive = 1;
            const uint KeepAliveIntervalMs = 30 * 1000;
            const uint KeepAliveRetryIntervalMs = 1 * 1000;

            //  struct tcp_keepalive
            //  {
            //      u_long  onoff;
            //      u_long  keepalivetime;
            //      u_long  keepaliveinterval;
            //  };
            byte[] keepAliveConfig = new byte[3 * sizeof(uint)];
            BitConverter.GetBytes(EnableKeepAlive).CopyTo(keepAliveConfig, 0);
            BitConverter.GetBytes(KeepAliveIntervalMs).CopyTo(keepAliveConfig, sizeof(uint));
            BitConverter.GetBytes(KeepAliveRetryIntervalMs).CopyTo(keepAliveConfig, 2 * sizeof(uint));
            return keepAliveConfig;
        }

        private static void SetReuseUnicastPort(Socket clientSocket)
        {
#if !NETSTANDARD15 && !NETSTANDARD16
            // This code should use RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            // but the feature is unavailable on .NET Framework 4.5.1.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try
                {
                    Debug.Assert(!clientSocket.IsBound);
                    // SocketOptionName.ReuseUnicastPort is only present in .NET Framework 4.6.1 and newer.
                    // Use the numeric value for as long as this code needs to target earlier versions.
                    const int SO_REUSE_UNICASTPORT = 0x3007;
                    clientSocket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_REUSE_UNICASTPORT, true);
                }
                catch (Exception e)
                {
                    DefaultTrace.TraceWarning("SetSocketOption(Socket, ReuseUnicastPort) failed: {0}", e);
                    // Ignore the exception.
                }
            }
#endif  // !NETSTANDARD15 && !NETSTANDARD16
        }
    }
}
