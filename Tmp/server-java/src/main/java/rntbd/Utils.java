package rntbd;

import io.netty.handler.codec.http.HttpResponseStatus;
import reactor.core.publisher.Mono;
import reactor.netty.NettyOutbound;
import reactor.netty.http.server.HttpServerResponse;

import java.util.UUID;

public class Utils {

    public static NettyOutbound OKResponseWithJsonBody(HttpServerResponse response,
                                                               String responseBody) {
        response.status(HttpResponseStatus.OK);
        Utils.AddDefaultHeaders(response,
                "application/json",
                responseBody.length());

        return response.sendString(Mono.just(responseBody));
    }

    public  static NettyOutbound OKResponseWithStringBody(HttpServerResponse response,
                                                          String responseBody) {
        response.status(HttpResponseStatus.OK);
        Utils.AddDefaultHeaders(response,
                "text/html; charset=UTF-8",
                responseBody.length());

        return response.sendString(Mono.just(responseBody));
    }

    public  static void AddDefaultHeaders(HttpServerResponse response,
                                          String contentType,
                                          int contentLength) {

        response
                .addHeader("Content-Type", contentType)
                .addHeader("x-ms-activity-id", UUID.randomUUID().toString())
                .addHeader("content-Length", Integer.toString(contentLength))
                .addHeader("x-ms-request-charge", "1.0")
                .addHeader("x-ms-session-token", "1000")
                .keepAlive(true);
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
