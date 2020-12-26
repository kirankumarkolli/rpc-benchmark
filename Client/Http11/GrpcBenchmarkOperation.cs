//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Grpc.Net.Client;
    using GrpcService;
    using static GrpcService.Greeter;

    internal class GrpcBenchmarkOperation : IBenchmarkOperation
    {
        static GreeterClient client;

        public GrpcBenchmarkOperation(BenchmarkConfig config)
        {
            if (client == null)
            {
                client = new GreeterClient(Utility.CreateGrpcChannel(config.EndPoint, config.MaxConnectionsPerServer()));
            }
        }

        public async Task ExecuteOnceAsync()
        {
            HelloReply helloReply = await GrpcBenchmarkOperation.client.SayHelloAsync(
                new GrpcService.HelloRequest()
                {
                    Name = Guid.NewGuid().ToString(),
                });
        }

        public Task PrepareAsync()
        {
            return Task.CompletedTask;
        }
    }
}
