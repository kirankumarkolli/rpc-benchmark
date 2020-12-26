using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace GrpcService
{
    public class GreeterService : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply
            {
                StatusCode = 200,
                Lsn = 9895,
                Message = GreeterService.testJsonPayload
            });
        }

        public static String testJsonPayload = "{\n" +
                "  \"data\": [{\n" +
                "    \"type\": \"articles\",\n" +
                "    \"id\": \"1\",\n" +
                "    \"attributes\": {\n" +
                "      \"title\": \"JSON:API paints my bikeshed!\",\n" +
                "      \"body\": \"The shortest article. Ever.\",\n" +
                "      \"created\": \"2015-05-22T14:56:29.000Z\",\n" +
                "      \"updated\": \"2015-05-22T14:56:28.000Z\"\n" +
                "    },\n" +
                "    \"relationships\": {\n" +
                "      \"author\": {\n" +
                "        \"data\": {\"id\": \"42\", \"type\": \"people\"}\n" +
                "      }\n" +
                "    }\n" +
                "  }],\n" +
                "  \"included\": [\n" +
                "    {\n" +
                "      \"type\": \"people\",\n" +
                "      \"id\": \"42\",\n" +
                "      \"attributes\": {\n" +
                "        \"name\": \"John\",\n" +
                "        \"age\": 80,\n" +
                "        \"gender\": \"male\"\n" +
                "      }\n" +
                "    }\n" +
                "  ]\n" +
                "}";

    }
}
