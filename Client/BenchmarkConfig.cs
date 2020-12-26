//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime;
    using System.Runtime.CompilerServices;
    using CommandLine;

    public class BenchmarkConfig
    {
        [Option('w', Required = true, HelpText = "Workload type insert, read")]
        public string WorkloadType { get; set; }

        [Option('e', Required = true, HelpText = "Cosmos account end point")]
        public string EndPoint { get; set; }

        [Option('n', Required = true, HelpText = "Number of documents to insert")]
        public int IterationCount { get; set; }

        [Option(Required = false, HelpText = "Enable latency percentiles")]
        public bool EnableLatencyPercentiles { get; set; }

        [Option(Required = false, HelpText = "Container partition key path")]
        public string PartitionKeyPath { get; set; } = "/partitionKey";

        [Option("pl", Required = true, HelpText = "Degree of parallism")]
        public int DegreeOfParallelism { get; set; }

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

        internal static bool UsePostManTarget { get; set; } = Debugger.IsAttached;
        internal static BenchmarkConfig From(string[] args)
        {
            if (BenchmarkConfig.UsePostManTarget)
            {
                return new PostManTestEndpointConfig();

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
            return this.DegreeOfParallelism;
        }

        virtual internal Uri RequestBaseUri()
        {
            return new Uri($"{this.EndPoint.TrimEnd('/')}/dbs/{this.Database}/cols/{this.Container}");
        }
    }

    internal class PostManTestEndpointConfig : BenchmarkConfig
    {
        public PostManTestEndpointConfig()
        {
            this.DegreeOfParallelism = 1;
            this.EndPoint = "https://postman-echo.com/";
            this.IterationCount = 100;
            this.WorkloadType = "Echo11Server";
            this.Database = "db1";
            this.Container = "col1";
        }

        internal override Uri RequestBaseUri()
        {
            return new Uri(this.EndPoint + "post");
        }
    }

}
