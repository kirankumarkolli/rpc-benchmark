namespace GrpcService
{
    using System;
    using System.IO;
    using System.Text;
    using CommandLine;
    using Newtonsoft.Json;

    public class ServiceConfig
    {
        [Option('w', Required = true, HelpText = "Http11, DotnetHttp2, ReactorHttp2, Grpc, Tcp")]
        public string WorkloadType { get; set; }


        internal static ServiceConfig From(string[] args)
        {
            ServiceConfig options = null;
            Parser.Default.ParseArguments<ServiceConfig>(args)
                .WithParsed<ServiceConfig>(e => options = e);

            return options;
        }

        internal void Print()
        {
            Console.WriteLine(ServiceConfig.ToString(this));
        }

        public static string ToString<T>(T input)
        {
            using (MemoryStream stream = ServiceConfig.ToStream(input))
            using (StreamReader sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }

        public static MemoryStream ToStream<T>(T input)
        {
            byte[] blob = new byte[4096];
            MemoryStream memStreamPayload = new MemoryStream(blob, 0, blob.Length, writable: true, publiclyVisible: true);
            memStreamPayload.SetLength(0);
            memStreamPayload.Position = 0;
            using (StreamWriter streamWriter = new StreamWriter(memStreamPayload,
                encoding: new UTF8Encoding(false, true),
                bufferSize: blob.Length,
                leaveOpen: true))
            {
                using (JsonWriter writer = new JsonTextWriter(streamWriter))
                {
                    ServiceConfig.serializer.Serialize(writer, input);
                    writer.Flush();
                    streamWriter.Flush();
                }
            }

            memStreamPayload.Position = 0;
            return memStreamPayload;
        }

        private static readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        });
    }
}
