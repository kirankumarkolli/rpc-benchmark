//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal class ParallelExecutionStrategy : IExecutionStrategy
    {
        private readonly Func<IBenchmarkOperation> benchmarkOperation;

        private volatile int pendingExecutorCount;

        public ParallelExecutionStrategy(
            Func<IBenchmarkOperation> benchmarkOperation)
        {
            this.benchmarkOperation = benchmarkOperation;
        }

        public async Task<RunSummary> ExecuteAsync(
            int serialExecutorConcurrency,
            int serialExecutorIterationCount,
            bool traceFailures,
            double warmupFraction)
        {
            int warmupPerIterationCount = (int)(serialExecutorIterationCount * warmupFraction) / serialExecutorConcurrency;
            await this.ExecuteAsyncInternal(
                serialExecutorConcurrency,
                warmupPerIterationCount,
                bWarmpup: true,
                traceFailures: traceFailures);

            Console.WriteLine("WARM-UP complete. Press ENTER to continue");
            Console.ReadLine();

            return await this.ExecuteAsyncInternal(
                serialExecutorConcurrency,
                serialExecutorIterationCount,
                bWarmpup: false,
                traceFailures: traceFailures);
        }

        private async Task<RunSummary> ExecuteAsyncInternal(
            int serialExecutorConcurrency,
            int serialExecutorIterationCount,
            bool bWarmpup,
            bool traceFailures)
        {
            string executorPrefix = bWarmpup ? "WarmUp" : string.Empty;

            IExecutor[] executors = new IExecutor[serialExecutorConcurrency];
            for (int i = 0; i < serialExecutorConcurrency; i++)
            {
                executors[i] = new SerialOperationExecutor(
                            executorId: executorPrefix + i.ToString(),
                            benchmarkOperation: this.benchmarkOperation());
            }

            this.pendingExecutorCount = serialExecutorConcurrency;
            for (int i = 0; i < serialExecutorConcurrency; i++)
            {
                _ = executors[i].ExecuteAsync(
                        iterationCount: serialExecutorIterationCount,
                        isWarmup: false,
                        traceFailures: traceFailures,
                        completionCallback: () => Interlocked.Decrement(ref this.pendingExecutorCount));
            }

            return await this.LogOutputStatsAndBlockedWait(executors, bWarmpup);
        }

        private async Task<RunSummary> LogOutputStatsAndBlockedWait(
            IExecutor[] executors,
            bool bWarmpup)
        {
            const int outputLoopDelayInSeconds = 1;
            IList<int> perLoopCounters = new List<int>();
            Summary lastSummary = new Summary();

            Stopwatch watch = new Stopwatch();
            watch.Start();

            bool isLastIterationCompleted = false;
            Console.WriteLine(bWarmpup ? "WarmUp Progress:" : "Workload Progress:");
            do
            {
                isLastIterationCompleted = this.pendingExecutorCount <= 0;

                Summary currentTotalSummary = new Summary();
                for (int i = 0; i < executors.Length; i++)
                {
                    IExecutor executor = executors[i];
                    Summary executorSummary = new Summary()
                    {
                        successfulOpsCount = executor.SuccessOperationCount,
                        failedOpsCount = executor.FailedOperationCount,
                        ruCharges = executor.TotalRuCharges,
                    };

                    currentTotalSummary += executorSummary;
                }

                // In-theory summary might be lower than real as its not transactional on time
                currentTotalSummary.elapsedMs = watch.Elapsed.TotalMilliseconds;

                Summary diff = currentTotalSummary - lastSummary;
                lastSummary = currentTotalSummary;

                if (!bWarmpup)
                {
                    diff.Print(currentTotalSummary.failedOpsCount + currentTotalSummary.successfulOpsCount);
                }
                perLoopCounters.Add((int)diff.Rps());

                await Task.Delay(TimeSpan.FromSeconds(outputLoopDelayInSeconds));
            }
            while (!isLastIterationCompleted);

            ConsoleColor consoleColor = bWarmpup ? ConsoleColor.DarkGray : ConsoleColor.Green;
            using (ConsoleColorContext ct = new ConsoleColorContext(consoleColor))
            {
                Console.WriteLine();
                Console.WriteLine(bWarmpup ? "WarmUp Summary:" : "Workload Summary:");
                Console.WriteLine("--------------------------------------------------------------------- ");
                lastSummary.Print(lastSummary.failedOpsCount + lastSummary.successfulOpsCount);

                // Skip first 5 and last 5 counters as outliers
                IEnumerable<int> exceptFirst5 = perLoopCounters.Skip(5);
                int[] summaryCounters = exceptFirst5.Take(exceptFirst5.Count() - 5).OrderByDescending(e => e).ToArray();

                RunSummary runSummary = new RunSummary();

                if (summaryCounters.Length > 10)
                {
                    Console.WriteLine();
                    Utility.TeeTraceInformation("After Excluding outliers");

                    runSummary.Top10PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.1 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top20PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.2 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top30PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.3 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top40PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.4 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top50PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.5 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top60PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.6 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top70PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.7 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top80PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.8 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top90PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.9 * summaryCounters.Length)).Average(), 0);
                    runSummary.Top95PercentAverageRps = Math.Round(summaryCounters.Take((int)(0.95 * summaryCounters.Length)).Average(), 0);
                    runSummary.AverageRps = Math.Round(summaryCounters.Average(), 0);

                    runSummary.Top50PercentLatencyInMs = TelemetrySpan.GetLatencyPercentile(50);
                    runSummary.Top75PercentLatencyInMs = TelemetrySpan.GetLatencyPercentile(75);
                    runSummary.Top90PercentLatencyInMs = TelemetrySpan.GetLatencyPercentile(90);
                    runSummary.Top95PercentLatencyInMs = TelemetrySpan.GetLatencyPercentile(95);
                    runSummary.Top99PercentLatencyInMs = TelemetrySpan.GetLatencyPercentile(99);

                    string summary = JsonHelper.ToString(runSummary);
                    Utility.TeeTraceInformation(summary);
                }
                else
                {
                    Utility.TeeTraceInformation("Please adjust ItemCount high to run of at-least 1M");
                }

                Console.WriteLine("--------------------------------------------------------------------- ");
                Console.WriteLine();
                return runSummary;
            }
        }
    }
}
