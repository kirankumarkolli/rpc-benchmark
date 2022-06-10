//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.CommandLine;
    using System.IO;
    using System.Linq;
    using System.Runtime;
    using System.CommandLine;
    using System.Threading.Tasks;

    internal class WorkloadTypeConfig
    {
        public string WorkloadType { get; set; }

        public int Concurrency { get; set; }

        public int MaxConnectionsPerEndpoint { get; set; }

        private static (string, int?, int?) ParseArgs(string[] args)
        {
            var workloadOption = new Option<string>(
                new string[] { "w", "WorkloadType" },
                description: "Workload types (DotNetHttp1, DotNetRntbd2, DotnetHttp2)")
            {
                IsRequired = true,
            };

            var concurencyOption = new Option<int>(
                new string[] { "c", "Concurrency" },
                description: "Concurrency of operations")
            {
                IsRequired = true,
            };

            var maxConnectionsPerEndpointOption = new Option<int>(
                new string[] { "m", "mcpe" },
                description: "Max connections per endpoint")
            {
                IsRequired = true,
            };

            var rootCommand = new RootCommand("RPC Benchmark Tool");
            rootCommand.AddOption(workloadOption);
            rootCommand.AddOption(concurencyOption);
            rootCommand.AddOption(maxConnectionsPerEndpointOption);

            string workload = null;
            int? concurrency = 0;
            int? mrpe = 0;
            rootCommand.SetHandler((invocationContext) =>
            {
                workload = invocationContext.ParseResult.CommandResult.GetValueForOption<string>(workloadOption);
                concurrency = invocationContext.ParseResult.CommandResult.GetValueForOption<int>(concurencyOption);
                mrpe = invocationContext.ParseResult.CommandResult.GetValueForOption<int>(maxConnectionsPerEndpointOption);
            });

            rootCommand.Invoke(args);

            return (workload, concurrency, mrpe);
        }

        internal static BenchmarkConfig From(string[] args)
        {
            (string workLoadType, int? concurrency, int? mrpe) = ParseArgs(args);

            WorkloadTypeConfig options = new WorkloadTypeConfig()
            {
                WorkloadType = workLoadType,
                Concurrency = concurrency.Value,
                MaxConnectionsPerEndpoint = mrpe.Value,
            };

            BenchmarkConfig config = null;
            switch (options.WorkloadType)
            {
                case "DotnetHttp1":
                    config = new DotnetHttp11EndpointConfig();
                    break;
                case "DotnetHttp2":
                    config = new DotnetHttp2EndpointConfig();
                    break;
                case "DotnetHttp3":
                    config = new DotnetHttp3EndpointConfig();
                    break;
                case "DotnetRntbd2":
                    config = new TcpServerEndpointConfig();
                    break;
                case "Grpc":
                    config = new GrpcEndpointConfig();
                    break;
                case "ReactorHttp2":
                    config = new ReactorHttp2EndpointConfig();
                    break;
                default:
                    throw new NotImplementedException();
            }

            config.MaxConnectionsPerEndpoint = options.MaxConnectionsPerEndpoint;
            config.DegreeOfParallelism = options.Concurrency;

            return config;
        }
    }

    public class BenchmarkConfig
    {
        public string WorkloadType { get; set; }

        public string EndPoint { get; set; }

        public int IterationCount { get; set; }

        public bool EnableLatencyPercentiles { get; set; } = true;

        public string PartitionKeyPath { get; set; } = "/partitionKey";

        public int DegreeOfParallelism { get; set; }

        public int MaxConnectionsPerEndpoint { get; set; }

        public string ItemTemplateFile { get; set; } = "Player.json";

        public bool TraceFailures { get; set; }

        public string Database { get; set; }

        public string Container { get; set; }

        internal void Print()
        {
            using (ConsoleColorContext ct = new ConsoleColorContext(ConsoleColor.Green))
            {
                Utility.TeeTraceInformation($"{nameof(BenchmarkConfig)} arguments");
                Utility.TeeTraceInformation($"IsServerGC: {GCSettings.IsServerGC}");
                Utility.TeeTraceInformation($"Test address: {this.RequestBaseUri()}");
                Utility.TeeTraceInformation("--------------------------------------------------------------------- ");
                Utility.TeeTraceInformation(JsonHelper.ToString(this));
                Utility.TeeTraceInformation("--------------------------------------------------------------------- ");
                Utility.TeeTraceInformation(string.Empty);
            }
        }

        virtual internal string ItemTemplatePayload()
        {
            return File.ReadAllText(this.ItemTemplateFile);
        }

        virtual internal int MaxConnectionsPerServer()
        {
            return this.MaxConnectionsPerEndpoint;
        }

        internal virtual Uri RequestBaseUri()
        {
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/db1/colls/col1/docs/item1");
        }
    }

    internal class TcpServerEndpointConfig : BenchmarkConfig
    {
        public TcpServerEndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://127.0.0.1:8009/";
            this.IterationCount = 1000000;
            this.WorkloadType = "TcpServer";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class GrpcEndpointConfig : BenchmarkConfig
    {
        public GrpcEndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:8083/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Grpc";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class DotnetHttp11EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp11EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:7070/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo11Server"; 
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class ReactorHttp2EndpointConfig : BenchmarkConfig
    {
        public ReactorHttp2EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:8080/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo20Server"; ;
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class DotnetHttp2EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp2EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:8080/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo20Server"; ;
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class DotnetHttp3EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp3EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:9090/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo30Server";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }
}
