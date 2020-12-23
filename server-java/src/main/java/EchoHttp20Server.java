import io.netty.channel.Channel;
import io.netty.handler.codec.http.HttpResponseStatus;
import io.netty.handler.codec.http2.Http2SecurityUtil;
import io.netty.handler.ssl.*;
import io.netty.handler.ssl.util.SelfSignedCertificate;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import reactor.core.publisher.Mono;
import reactor.netty.ChannelPipelineConfigurer;
import reactor.netty.ConnectionObserver;
import reactor.netty.DisposableServer;
import reactor.netty.http.HttpProtocol;
import reactor.netty.http.server.HttpServer;

import javax.net.ssl.SSLException;
import java.net.SocketAddress;
import java.security.cert.CertificateException;

public class EchoHttp20Server extends EchoServerBase {
    private final static Logger logger = LoggerFactory.getLogger(EchoHttp11Server.class);

    @Override
    public void Start(int port) throws CertificateException, SSLException {
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

        server = HttpServer.create()
                .host("localhost")
                .port(port)
                .protocol(HttpProtocol.H2)
                .secure(spec -> spec.sslContext(sslCtx))
                .route(routes -> {
                    routes.get("/hello",
                            (request, response) ->
                                    Utils.OKResponseWithJsonBody(response, Utils.testJsonPayload)
                    );
                })
                .route(routes -> {
                    routes.get("/dbs/{dbid}", // colls/{collid}/docs/{docid}
                            (request, response) ->
                            {
//                                logger.debug("/dbs/{}/colls/{}/docs/{}",
//                                        request.param("dbid"),
//                                        request.param("collid"),
//                                        request.param("docid"));
                                return Utils.OKResponseWithJsonBody(response, Utils.testJsonPayload);
                            }
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

        logger.info("Http2 Server listening on port: {}", port);
    }
}
