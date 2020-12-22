//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;

    internal static class Utility
    {
        public static void TeeTraceInformation(string payload)
        {
            Console.WriteLine(payload);
            Trace.TraceInformation(payload);
        }

        public static void TeePrint(string format, params object[] arg)
        {
            string payload = string.Format(format, arg);
            Utility.TeeTraceInformation(payload);
        }

        public static HttpClient CreateHttp1Client(int maxConnectionsPerServer)
        {
            return Utility.CreateHttpClient(maxConnectionsPerServer);
        }

        public static HttpClient CreateHttp2Client(int maxConnectionsPerServer)
        {
            HttpClient client = Utility.CreateHttpClient(maxConnectionsPerServer);
            client.DefaultRequestVersion = new Version(2, 0);

            return client;
        }

        private static HttpClient CreateHttpClient(int maxConnectionsPerServer)
        {
            HttpClientHandler httpClientHandler = new HttpClientHandler
            {
                MaxConnectionsPerServer = maxConnectionsPerServer,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };

            HttpClient client = new HttpClient(httpClientHandler, disposeHandler: true);
            client.DefaultRequestHeaders
                  .Accept
                  .Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header

            return client;
        }

    }
}
