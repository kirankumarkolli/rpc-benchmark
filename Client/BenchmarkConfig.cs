﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime;
    using CommandLine;

    public class WorkloadTypeConfig
    {
        [Option('w', Required = true, HelpText = "Http11, DotnetHttp2, ReactorHttp2, Grpc, Tcp")]
        public string WorkloadType { get; set; }

        [Option('e', Required = true, HelpText = "Endpoint IP address")]
        public string Endpoint { get; set; }

        [Option('c', Required = true, HelpText = "Concurrency")]
        public int Parallism { get; set; }

        [Option("mcpe", Required = true, HelpText = "Max connections per endpoint")]
        public int MaxConnectionsPerEndpoint { get; set; }

        internal static BenchmarkConfig From(string[] args)
        {
            WorkloadTypeConfig options = null;
            Parser.Default.ParseArguments<WorkloadTypeConfig>(args)
                .WithParsed<WorkloadTypeConfig>(e => options = e);

            BenchmarkConfig config = null;
            switch (options.WorkloadType)
            {
                case "Http11":
                    config = new DotnetHttp11EndpointConfig();
                    break;
                case "DotnetHttp2":
                    config = new DotnetHttp2EndpointConfig();
                    break;
                case "Grpc":
                    config = new GrpcEndpointConfig();
                    break;
                case "ReactorHttp2":
                    config = new ReactorHttp2EndpointConfig();
                    break;
                case "Tcp":
                    config = new TcpServerEndpointConfig();
                    break;
                case "Http3":
                    config = new DotnetHttp3EndpointConfig();
                    break;
                default:
                    throw new NotImplementedException();
            }

            // Patch the endpoint
            if (!string.IsNullOrWhiteSpace(options.Endpoint))
            {
                Uri defaultEndpoint = new Uri(config.EndPoint);
                config.EndPoint = string.Format($"{defaultEndpoint.Scheme}://{options.Endpoint}:{defaultEndpoint.Port}");
            }

            config.MaxConnectionsPerEndpoint = options.MaxConnectionsPerEndpoint;
            config.DegreeOfParallelism = options.Parallism;

            return config;
        }
    }

    public class BenchmarkConfig
    {
        [Option('w', Required = true, HelpText = "Workload type insert, read")]
        public string WorkloadType { get; set; }

        [Option('e', Required = true, HelpText = "Cosmos account end point")]
        public string EndPoint { get; set; }

        [Option('n', Required = true, HelpText = "Number of documents to insert")]
        public int IterationCount { get; set; }

        [Option(Required = false, HelpText = "Enable latency percentiles")]
        public bool EnableLatencyPercentiles { get; set; } = true;

        [Option(Required = false, HelpText = "Container partition key path")]
        public string PartitionKeyPath { get; set; } = "/partitionKey";

        [Option("pl", Required = true, HelpText = "Degree of parallism")]
        public int DegreeOfParallelism { get; set; }

        public int MaxConnectionsPerEndpoint { get; set; }

        [Option(Required = false, HelpText = "Item template")]
        public string ItemTemplateFile { get; set; } = "Player.json";

        [Option(Required = false, HelpText = "Write the task execution failure to console. Useful for debugging failures")]
        public bool TraceFailures { get; set; }

        [Option(Required = true, HelpText = "Database to use")]
        public string Database { get; set; }

        [Option(Required = true, HelpText = "Collection to use")]
        public string Container { get; set; }
        internal void Print()
        {
            using (ConsoleColorContext ct = new ConsoleColorContext(ConsoleColor.Green))
            {
                Utility.TeeTraceInformation($"{nameof(BenchmarkConfig)} arguments");
                Utility.TeeTraceInformation($"IsServerGC: {GCSettings.IsServerGC}");
                Utility.TeeTraceInformation("--------------------------------------------------------------------- ");
                Utility.TeeTraceInformation(JsonHelper.ToString(this));
                Utility.TeeTraceInformation("--------------------------------------------------------------------- ");
                Utility.TeeTraceInformation(string.Empty);
            }
        }

        internal static bool DebuggerStartedConfig { get; set; } = true; // Debugger.IsAttached;
        internal static BenchmarkConfig From(string[] args)
        {
            if (BenchmarkConfig.DebuggerStartedConfig)
            {
                return new DotnetHttp2EndpointConfig();
            }

            BenchmarkConfig options = null;
            Parser.Default.ParseArguments<BenchmarkConfig>(args)
                .WithParsed<BenchmarkConfig>(e => options = e)
                .WithNotParsed<BenchmarkConfig>(e => BenchmarkConfig.HandleParseError(e));

            return options;
        }

        private static void HandleParseError(IEnumerable<Error> errors)
        {
            using (ConsoleColorContext ct = new ConsoleColorContext(ConsoleColor.Red))
            {
                foreach (Error e in errors)
                {
                    Console.WriteLine(e.ToString());
                }
            }

            Environment.Exit(errors.Count());
        }

        virtual internal string ItemTemplatePayload()
        {
            return File.ReadAllText(this.ItemTemplateFile);
        }

        virtual internal int MaxConnectionsPerServer()
        {
            return this.MaxConnectionsPerEndpoint;
        }

        virtual internal Uri RequestBaseUri()
        {
            // return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/{this.Database}/cols/{this.Container}");
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
        }
    }

    internal class TcpServerEndpointConfig : BenchmarkConfig
    {
        public TcpServerEndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            //this.EndPoint = "https://postman-echo.com/get?foo1=bar1&foo2=bar2";
            this.EndPoint = "https://localhost:8082/";
            this.IterationCount = 1000000;
            this.WorkloadType = "TcpServer";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }

        internal override Uri RequestBaseUri()
        {
            //return new Uri(this.EndPoint);
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
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

        internal override Uri RequestBaseUri()
        {
            //return new Uri(this.EndPoint);
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
        }
    }

    internal class DotnetHttp11EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp11EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            //this.EndPoint = "https://postman-echo.com/get?foo1=bar1&foo2=bar2";
            this.EndPoint = "https://localhost:8080/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo11Server"; 
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }

        internal override Uri RequestBaseUri()
        {
            //return new Uri(this.EndPoint);
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
        }
    }

    internal class ReactorHttp2EndpointConfig : BenchmarkConfig
    {
        public ReactorHttp2EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:8081/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo20Server"; ;
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }

        internal override Uri RequestBaseUri()
        {
            //return new Uri(this.EndPoint);
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
        }
    }

    internal class DotnetHttp2EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp2EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:8091/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo20Server"; ;
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }

        internal override Uri RequestBaseUri()
        {
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
        }
    }

    internal class DotnetHttp3EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp3EndpointConfig()
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = "https://localhost:8093/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo30Server";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }

        internal override Uri RequestBaseUri()
        {
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/");
        }
    }
}
