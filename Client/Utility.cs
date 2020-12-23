//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
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
            ServicePointManager.UseNagleAlgorithm = false;
            SocketsHttpHandler httpClientHandler = new SocketsHttpHandler()
            {
                MaxConnectionsPerServer = maxConnectionsPerServer,
                SslOptions = new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = 
                        (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true
                }
            };

            HttpClient client = new HttpClient(httpClientHandler, disposeHandler: true);
            client.DefaultRequestHeaders
                  .Accept
                  .Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header

            return client;
        }

    }
}
