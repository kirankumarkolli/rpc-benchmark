//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Diagnostics;
    using System.Net;
    using Microsoft.Azure.Cosmos;

    /// <summary>
    /// This sample demonstrates how to achieve high performance writes using Azure Comsos DB.
    /// </summary>
    public sealed class Program
    {
        /// <summary>
        /// Main method for the sample.
        /// </summary>
        /// <param name="args">command line arguments.</param>
        public static async Task Main(string[] args)
        {
            try
            {
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.ReusePort = true;

                BenchmarkConfig config = BenchmarkConfig.From(args);
                ThreadPool.SetMinThreads(config.DegreeOfParallelism, config.DegreeOfParallelism);

                if (config.EnableLatencyPercentiles)
                {
                    TelemetrySpan.IncludePercentile = true;
                    TelemetrySpan.ResetLatencyHistogram(config.IterationCount);
                }

                config.Print();

                Program program = new Program();
                await program.ExecuteAsync(config);
            }
            finally
            {
                Console.WriteLine($"{nameof(CosmosBenchmark)} completed successfully.");
                if (Debugger.IsAttached)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadLine();
                }
            }
        }

        /// <summary>
        /// Run samples for Order By queries.
        /// </summary>
        /// <returns>a Task object.</returns>
        private async Task ExecuteAsync(BenchmarkConfig config)
        {
            int opsPerTask = config.IterationCount / config.DegreeOfParallelism;
            int taskCount = config.DegreeOfParallelism;

            Func<IBenchmarkOperation> benchmarkOperationFactory = this.GetBenchmarkFactoryMethod(config);

            IExecutionStrategy execution = IExecutionStrategy.StartNew(config, benchmarkOperationFactory);
            RunSummary runSummary = await execution.ExecuteAsync(taskCount, opsPerTask, config.TraceFailures, 0.01);

            runSummary.WorkloadType = config.WorkloadType;
            runSummary.Date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            runSummary.Time = DateTime.UtcNow.ToString("HH-mm");
            runSummary.TotalOps = config.IterationCount;
            runSummary.Concurrency = taskCount;
            runSummary.AccountName = config.EndPoint;
        }

        private Func<IBenchmarkOperation> GetBenchmarkFactoryMethod(BenchmarkConfig config)
        {

            Type[] availableBenchmarks = Program.AvailableBenchmarks();
            IEnumerable<Type> res = availableBenchmarks
                .Where(e => e.Name.Equals(config.WorkloadType, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(config.WorkloadType + "BenchmarkOperation", StringComparison.OrdinalIgnoreCase));

            if (res.Count() != 1)
            {
                throw new NotImplementedException($"Unsupported workload type {config.WorkloadType}. Available ones are " +
                    string.Join(", \r\n", availableBenchmarks.Select(e => e.Name)));
            }

            Type benchmarkTypeName = res.Single();

            ConstructorInfo ci = benchmarkTypeName.GetConstructor(new Type[] { typeof(BenchmarkConfig) });
            object[] ctorArguments = new object[]
                    {
                        config
                    };

            return () => (IBenchmarkOperation)ci.Invoke(ctorArguments);
        }

        private static Type[] AvailableBenchmarks()
        {
            Type benchmarkType = typeof(IBenchmarkOperation);
            return typeof(Program).Assembly.GetTypes()
                .Where(p => benchmarkType.IsAssignableFrom(p))
                .ToArray();
        }
    }
}
