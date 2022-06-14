//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using CommandLine;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime;

    public class WorkloadTypeConfig
    {
        [Option(shortName: 'w', longName: "WorkloadType", Required = true, HelpText = "Workload types (DotNetHttp1, DotNetRntbd2, DotnetHttp2, Grpc, ReactorHttp2, Http3)")]
        public string WorkloadType { get; set; }

        [Option(shortName: 'c', longName: "Concurrency", Required = false, HelpText = "Concurrency")]
        public int Concurrency { get; set; }

        [Option(shortName: 'm', longName: "MaxConnectionsPerEndpoint", Required = false, HelpText = "Max connections per endpoint")]
        public int MaxConnectionsPerEndpoint { get; set; }

        [Option(shortName: 'h', longName: "Hostname", Required = false, Default = "localhost", HelpText = "Target hostname")]
        public string Hostname { get; set; }

        internal static BenchmarkConfig From(string[] args)
        {
            // fabianm DEBUG HINT
            WorkloadTypeConfig options = null;
            ParserResult<WorkloadTypeConfig> parserResult = Parser.Default.ParseArguments<WorkloadTypeConfig>(args);
            parserResult.WithParsed<WorkloadTypeConfig>(e => options = e);
            parserResult.WithNotParsed(errors => HandleParseError(errors));

            BenchmarkConfig config = null;
            switch (options.WorkloadType)
            {
                case "DotnetHttp1":
                    config = new DotnetHttp11EndpointConfig(parserResult.Value.Hostname);
                    break;
                case "DotnetHttp2":
                    config = new DotnetHttp2EndpointConfig(parserResult.Value.Hostname);
                    break;
                case "DotnetHttp3":
                    config = new DotnetHttp3EndpointConfig(parserResult.Value.Hostname);
                    break;
                case "DotnetRntbd2":
                    config = new TcpServerEndpointConfig(parserResult.Value.Hostname);
                    break;
                case "Grpc":
                    config = new GrpcEndpointConfig(parserResult.Value.Hostname);
                    break;
                case "ReactorHttp2":
                    config = new ReactorHttp2EndpointConfig(parserResult.Value.Hostname);
                    break;
                default:
                    throw new NotImplementedException();
            }

            config.MaxConnectionsPerEndpoint = options.MaxConnectionsPerEndpoint;
            config.DegreeOfParallelism = options.Concurrency;

            return config;
        }

        internal static void HandleParseError(IEnumerable<Error> errors)
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
        public TcpServerEndpointConfig(string hostname)
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = $"https://{hostname}:8009/";
            this.IterationCount = 1000000;
            this.WorkloadType = "TcpServer";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class GrpcEndpointConfig : BenchmarkConfig
    {
        public GrpcEndpointConfig(string hostname)
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = $"https://{hostname}:8083/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Grpc";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class DotnetHttp11EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp11EndpointConfig(string hostname)
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = $"https://{hostname}:7070/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo11Server"; 
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class ReactorHttp2EndpointConfig : BenchmarkConfig
    {
        public ReactorHttp2EndpointConfig(string hostname)
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = $"https://{hostname}:8080/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo20Server"; ;
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class DotnetHttp2EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp2EndpointConfig(string hostname)
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = $"https://{hostname}:8080/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo20Server"; ;
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }

    internal class DotnetHttp3EndpointConfig : BenchmarkConfig
    {
        public DotnetHttp3EndpointConfig(string hostname)
        {
            this.DegreeOfParallelism = 5;
            this.EndPoint = $"https://{hostname}:9090/";
            this.IterationCount = 1000000;
            this.WorkloadType = "Echo30Server";
            this.Database = "db1";
            this.Container = "col1";
            this.EnableLatencyPercentiles = true;
        }
    }
}
