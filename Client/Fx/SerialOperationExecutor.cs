//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;

    internal class SerialOperationExecutor : IExecutor
    {
        private readonly IBenchmarkOperation operation;
        private readonly string executorId;

        public SerialOperationExecutor(
            string executorId,
            IBenchmarkOperation benchmarkOperation)
        {
            this.executorId = executorId;
            this.operation = benchmarkOperation;

            this.SuccessOperationCount = 0;
            this.FailedOperationCount = 0;
        }

        public int SuccessOperationCount { get; private set; }
        public int FailedOperationCount { get; private set; }

        public double TotalRuCharges { get; private set; }

        public async Task ExecuteAsync(
                int iterationCount,
                bool isWarmup,
                bool traceFailures,
                Action completionCallback)
        {
            Trace.TraceInformation($"Executor {this.executorId} started");

            try
            {
                int currentIterationCount = 0;
                do
                {
                    await this.operation.PrepareAsync();

                    using (IDisposable telemetrySpan = TelemetrySpan.StartNew(
                                disableTelemetry: isWarmup))
                    {
                        try
                        {
                            await this.operation.ExecuteOnceAsync();

                            // Success case
                            this.SuccessOperationCount++;
                        }
                        catch (Exception ex)
                        {
                            if (traceFailures)
                            {
                                Console.WriteLine(ex.ToString());
                            }

                            // failure case
                            this.FailedOperationCount++;
                        }
                    }

                    currentIterationCount++;
                } while (currentIterationCount < iterationCount);

                Trace.TraceInformation($"Executor {this.executorId} completed");
            }
            finally
            {
                completionCallback();
            }
        }
    }
}
