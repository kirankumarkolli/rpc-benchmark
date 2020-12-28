namespace GrpcService
{
    using CommandLine;

    public class WorkloadTypeConfig
    {
        [Option('w', Required = true, HelpText = "Http11, DotnetHttp2, ReactorHttp2, Grpc, Tcp")]
        public string WorkloadType { get; set; }


        internal static WorkloadTypeConfig From(string[] args)
        {
            WorkloadTypeConfig options = null;
            Parser.Default.ParseArguments<WorkloadTypeConfig>(args)
                .WithParsed<WorkloadTypeConfig>(e => options = e);

            return options;
        }
    }
}
