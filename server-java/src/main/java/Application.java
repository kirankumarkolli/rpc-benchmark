import io.netty.channel.Channel;
import io.netty.handler.codec.http.HttpResponseStatus;
import io.netty.handler.codec.http2.Http2SecurityUtil;
import io.netty.handler.ssl.*;
import io.netty.handler.ssl.util.SelfSignedCertificate;
import reactor.core.publisher.Mono;
import reactor.netty.ChannelPipelineConfigurer;
import reactor.netty.ConnectionObserver;
import reactor.netty.DisposableServer;
import reactor.netty.http.HttpProtocol;
import reactor.netty.http.server.HttpServer;

import java.net.SocketAddress;
import java.security.cert.CertificateException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.net.ssl.SSLException;

public class Application {
    private final static Logger logger = LoggerFactory.getLogger(Application.class);

    public static void main(String[] args) throws CertificateException, SSLException {
        logger.trace("TRACE message");
        logger.debug("DEBUG message");
        logger.info("INFO message");
        logger.warn("WARN message");
        logger.error("ERROR message");

        //runHttp1Server(8081);
        runHttp2Server(8080);
    }

    public static void runHttp1Server(int port) throws CertificateException, SSLException {
        SelfSignedCertificate ssc = new SelfSignedCertificate("localhost");
        SslContext sslCtx = SslContextBuilder.forServer(ssc.certificate(), ssc.privateKey())
//                .startTls(true)
                .build();

        DisposableServer server =
                HttpServer.create()
                        .port(port)
                        .protocol(HttpProtocol.HTTP11)
                        .secure(spec -> spec.sslContext(sslCtx))
                        .route(routes -> {
                                routes.get("/hello",
                                        (request, response) ->
                                            response
                                                    .status(HttpResponseStatus.OK)
                                                    .keepAlive(true)
                                                    .sendString(Mono.just("Hello world"))
                                        );
                            })
                        .wiretap(true)
                        .doOnConnection(connection ->
                                {
                                    if (logger.isInfoEnabled()) {
                                        logger.info("OnConnection for: {}", connection.address());
                                    }
                                })
                        .doOnChannelInit(new ChannelPipelineConfigurer() {
                                @Override
                                public void onChannelInit(ConnectionObserver connectionObserver, Channel channel, SocketAddress socketAddress) {
                                    if (logger.isInfoEnabled()) {
                                        logger.info("OnChannelInit for: {} -> ", channel.remoteAddress(), channel.localAddress());
                                    }
                                }
                            })
                        .bindNow();

        logger.info("Server listening on port: {}", port);

        server
            .onDispose()
            .block();
    }

    public static void runHttp2Server(int port) throws CertificateException, SSLException {
        SelfSignedCertificate ssc = new SelfSignedCertificate();
        SslContext sslCtx = SslContextBuilder.forServer(ssc.certificate(), ssc.privateKey())
//                .startTls(true)
                .ciphers(Http2SecurityUtil.CIPHERS, SupportedCipherSuiteFilter.INSTANCE)
                .applicationProtocolConfig(
                        new ApplicationProtocolConfig(ApplicationProtocolConfig.Protocol.ALPN,
                                ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE,
                                ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT,
                                ApplicationProtocolNames.HTTP_2))
                .build();

        DisposableServer server =
                HttpServer.create()
                        .host("localhost")
                        .port(port)
                        .protocol(HttpProtocol.H2)
                        .secure(spec -> spec.sslContext(sslCtx))
                        .route(routes -> {
                            routes.get("/hello",
                                    (request, response) ->
                                            response
                                                    .status(HttpResponseStatus.OK)
                                                    .sendString(Mono.just("Hello world"))
                            );
                        })
                        .doOnConnection(connection ->
                        {
                            if (logger.isInfoEnabled()) {
                                logger.info("OnConnection for: {}", connection.address());
                            }
                        })
                        .doOnChannelInit(new ChannelPipelineConfigurer() {
                            @Override
                            public void onChannelInit(ConnectionObserver connectionObserver, Channel channel, SocketAddress socketAddress) {
                                if (logger.isInfoEnabled()) {
                                    logger.info("OnChannelInit for: {} -> ", channel.remoteAddress(), channel.localAddress());
                                }
                            }
                        })
                        .bindNow();

        logger.info("Server listening on port: {}", port);

        server
                .onDispose()
                .block();
    }
}