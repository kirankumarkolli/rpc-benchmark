//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using Microsoft.Azure.Cosmos;

    /// <summary>
    /// This sample demonstrates how to achieve high performance writes using Azure Comsos DB.
    /// </summary>
    public sealed class Program
    {
        private static int counter = 0;

        /// <summary>
        /// Main method for the sample.
        /// </summary>
        /// <param name="args">command line arguments.</param>
        public static async Task Main(string[] args)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            int concurrency = 10;
            int perTaskIterations = 50000;
            Task<Dictionary<uint, int>>[] allTasks = new Task<Dictionary<uint, int>>[concurrency];

            for (int i = 0; i < concurrency; i++)
            {
                allTasks[i] = OneClientWorkRetryAsync(i, perTaskIterations);
            }

            await Task.WhenAll(allTasks);
            stopwatch.Stop();

            Dictionary<uint, int> totalTaskResult = allTasks
                        .Select(d => d.Result)
                        .SelectMany(d => d)
                        .ToLookup(pair => pair.Key, pair => pair.Value)
                        .ToDictionary(group => group.Key, group => group.Sum());
            foreach(var e in totalTaskResult)
            {
                Console.WriteLine($"{e.Key} -> {e.Value}");
            }

            Console.WriteLine($"Execution time: {stopwatch.ElapsedMilliseconds}");
            Console.WriteLine("Press ENTER to terminate");
            Console.ReadLine();
        }


        private static async Task<Dictionary<uint, int>> OneClientWorkRetryAsync(int clentId,
            int executions)
        {
            Dictionary<uint, int> result = new Dictionary<uint, int>();

            while(executions - result.Values.Sum() > 0)
            {
                Dictionary<uint, int> iterResult = await OneClientWorkAsync(clentId, executions - result.Count);
                result = result.Concat(iterResult)
                            .ToLookup(pair => pair.Key, pair => pair.Value)
                            .ToDictionary(group => group.Key, group => group.Sum());
            }

            return result;
        }

        private static async Task<Dictionary<uint, int>> OneClientWorkAsync(int clentId,
            int executions)
        {
            //string replicaPath = "rntbd://cdb-ms-prod-westus1-fd53.documents.azure.com:14104/apps/4e361f12-a2d0-43ad-b9d4-d63106b2239c/services/8866737c-a179-49f5-a0ce-202391d96417/partitions/008a0b37-198c-4265-80a5-618d1fc2e2f7/replicas/133021509114696822s/";
            //string replicaPath = "rntbd://127.0.0.1:10251/apps/DocDbApp/services/DocDbServer7/partitions/a4cb4953-38c8-11e6-8106-8cdcd42c33be/replicas/1p";
            //"/apps/DocDbApp/services/DocDbMaster0/partitions/780e44f4-38c8-11e6-8106-8cdcd42c33be/replicas/1p"
            //"/apps/DocDbApp/services/DocDbServer7/partitions/a4cb4953-38c8-11e6-8106-8cdcd42c33be/replicas/1p"

            // string replicaPath = "rntbd://127.0.0.1:10253/apps/DocDbApp/services/DocDbServer7/partitions/a4cb4953-38c8-11e6-8106-8cdcd42c33be/replicas/1p";
            string replicaPath = "https://localhost:8009/127.0.0.1:10253/apps/DocDbApp/services/DocDbServer7/partitions/a4cb4953-38c8-11e6-8106-8cdcd42c33be/replicas/1p";
            Uri replicaPathUri = new Uri(replicaPath);
            int newValue = 0;

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            Task<Dictionary<uint, int>> receiveLoopTask = null;

            try
            {
                using (CosmosDuplexPipe duplexPipe = await CosmosDuplexPipe.ConnectAsClientAsync(
                                    replicaPathUri,
                                    cancellationToken: cancellationTokenSource.Token))
                {
                    // Dummy increment to keep receiveloop
                    Interlocked.Increment(ref duplexPipe.inflightRequests);
                    receiveLoopTask = duplexPipe.ReceiveToNULL(cancellationTokenSource.Token);

                    while (executions > 0)
                    {
                        newValue = Interlocked.Increment(ref counter);

                        long inflightRequests = Interlocked.Read(ref duplexPipe.inflightRequests);
                        if (inflightRequests > 10)
                        {
                            TimeSpan waitTime = TimeSpan.FromMilliseconds(inflightRequests * 5);
                            // Console.WriteLine($"ClientID:{clentId} Iteration: {newValue}  inflightRequests: {inflightRequests} paused for MS: {waitTime.TotalMilliseconds}");
                            await Task.Delay(waitTime);
                        }

                        await duplexPipe.SubmitReadReqeustAsync(replicaPathUri.PathAndQuery, "db1", "col1", "id1", @"[""id1""]");

                        if (newValue % 100 == 0)
                        {
                            // Console.WriteLine($"ClientID:{clentId} Iteration: {newValue} inflightRequests: {inflightRequests} UnflushedBytes(writer): {duplexPipe.Writer.PipeWriter.UnflushedBytes}");
                        }

                        executions--;
                    }

                    Interlocked.Decrement(ref duplexPipe.inflightRequests);
                    Dictionary<uint, int> statusCodes = await receiveLoopTask;

                    Console.WriteLine($"ClientID:{clentId} Completed: {executions} #statusCodes: {statusCodes.Count}");
                    return statusCodes;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{newValue} -> {ex.ToString()}");
            }
            finally
            {
                cancellationTokenSource.Cancel();
            }

            cancellationTokenSource.Cancel();
            if(receiveLoopTask != null)
            {
                return await receiveLoopTask;
            }

            return new Dictionary<uint, int>();
        }

        /// <summary>
        /// Main method for the sample.
        /// </summary>
        /// <param name="args">command line arguments.</param>
        public static async Task Main1(string[] args)
        {
            try
            {
                //AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);

                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.ReusePort = true;

                BenchmarkConfig config = WorkloadTypeConfig.From(args);
                ThreadPool.SetMinThreads(config.DegreeOfParallelism, config.DegreeOfParallelism);

                if (config.EnableLatencyPercentiles)
                {
                    TelemetrySpan.IncludePercentile = true;
                    TelemetrySpan.ResetLatencyHistogram(config.IterationCount);
                }

                config.Print();

                //Echo30ServerBenchmarkOperation operation = new Echo30ServerBenchmarkOperation(config);
                //await operation.ExecuteOnceAsync();

                Program program = new Program();
                RunSummary runSummary = await program.ExecuteAsync(config);
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
        private async Task<RunSummary> ExecuteAsync(BenchmarkConfig config)
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

            return runSummary;
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
