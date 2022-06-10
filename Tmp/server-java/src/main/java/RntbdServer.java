import io.netty.bootstrap.ServerBootstrap;
import io.netty.channel.ChannelInitializer;
import io.netty.channel.ChannelOption;
import io.netty.channel.ChannelPipeline;
import io.netty.channel.nio.NioEventLoopGroup;
import io.netty.channel.socket.SocketChannel;
import io.netty.channel.socket.nio.NioServerSocketChannel;
import io.netty.handler.logging.LogLevel;
import io.netty.handler.logging.LoggingHandler;
import io.netty.handler.ssl.SslContext;
import io.netty.handler.ssl.SslContextBuilder;
import io.netty.handler.ssl.SslHandler;
import io.netty.handler.ssl.util.SelfSignedCertificate;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import rntbd.*;

import javax.net.ssl.SSLEngine;
import javax.net.ssl.SSLException;
import java.security.cert.CertificateException;
import java.util.concurrent.ExecutionException;

public class RntbdServer extends EchoServerBase {
    private final static Logger logger = LoggerFactory.getLogger(RntbdServer.class);

    @Override
    public void Start(int port) throws CertificateException, SSLException, ExecutionException, InterruptedException {
//        SelfSignedCertificate ssc = new SelfSignedCertificate("localhost");
//        SslContext sslCtx = SslContextBuilder.forServer(ssc.certificate(), ssc.privateKey())
//                .build();
//
//        server =
//                TcpServer.create()
//                        .host("localhost")
//                        .port(port)
//                        .secure(spec -> spec.sslContext(sslCtx))
//                        //.handle((inbound, outbound) -> outbound)
//                        .option(ChannelOption.SO_KEEPALIVE, true)
//                        .doOnConnection(conn -> {
//                                conn.addHandlerLast(new ServerRntbdRequestFramer());
//                                conn.addHandlerLast(new RntbdCodec());
//                                conn.addHandlerLast(new ServerRntbdRequestProcessor());
//
//                                ChannelPipeline pipeline = conn.channel().pipeline();
//                                logger.warn("Pipeline names are: {}", pipeline.names());
//                            })
//                        .wiretap(true)
//                        .bindNow();

        bootstrapNativeNettyStart(port);
        logger.info("RntbdServer listening on port: {}", port);
    }

    private void bootstrapNativeNettyStart(int port) throws CertificateException, SSLException, ExecutionException, InterruptedException {
        ServerBootstrap bootstrap = new ServerBootstrap();
        NioEventLoopGroup parent = new NioEventLoopGroup();
        NioEventLoopGroup child = new NioEventLoopGroup();

        SelfSignedCertificate ssc = new SelfSignedCertificate("localhost");
        SslContext sslContext = SslContextBuilder.forServer(ssc.certificate(), ssc.privateKey())
                .build();

        LogLevel logLevel = LogLevel.WARN;
        if (logger.isTraceEnabled()) {
            logLevel = LogLevel.TRACE;
        } else if (logger.isDebugEnabled()) {
            logLevel = LogLevel.DEBUG;
        } else if (logger.isInfoEnabled()) {
            logLevel = LogLevel.INFO;
        } else if (logger.isErrorEnabled()) {
            logLevel = LogLevel.ERROR;
        }

        final LogLevel finalLogLevel = logLevel;

        bootstrap.group(parent, child)
                .channel(NioServerSocketChannel.class)
                .childHandler(new ChannelInitializer<SocketChannel>() {
                    @Override
                    public void initChannel(SocketChannel channel) throws Exception {

                        SSLEngine engine = sslContext.newEngine(channel.alloc());
                        engine.setUseClientMode(false);

                        ChannelPipeline pipeline = channel.pipeline();
                        pipeline.addLast(
                                new SslHandler(engine),
                                //new LoggingHandler(finalLogLevel),
                                new RntbdRequestFrameInAdapter(),
                                new RntbdCodec(),
                                new EchoRntbdRequestProcessor()
                        );

                        logger.warn("[cid: 0x{}] created a new pipeline", channel.id());
                    }
                })
                .childOption(ChannelOption.SO_KEEPALIVE, true);

        bootstrap.bind(port).get();

        logger.warn("RntbdServer listening on port: {}", port);
    }
}
