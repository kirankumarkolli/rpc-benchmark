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

        IServer http1Server = runHttp1Server(8080);
        IServer http2Server = runHttp2Server(8081);

        http1Server.BlockedWait();
        http2Server.BlockedWait();
    }

    public static IServer runHttp1Server(int port) throws CertificateException, SSLException {

        EchoHttp11Server server = new EchoHttp11Server();
        server.Start(port);

        return  server;
    }

    public static IServer runHttp2Server(int port) throws CertificateException, SSLException {
        EchoHttp20Server server = new EchoHttp20Server();
        server.Start(port);

        return  server;
   }
}