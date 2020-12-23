import io.netty.handler.codec.http.HttpResponseStatus;
import reactor.core.publisher.Mono;
import reactor.netty.NettyOutbound;
import reactor.netty.http.server.HttpServerResponse;

import java.util.UUID;

public class Utils {
    public  static NettyOutbound OKResponseWithJsonBody(HttpServerResponse response,
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
}
