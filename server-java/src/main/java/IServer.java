import javax.net.ssl.SSLException;
import java.security.cert.CertificateException;
import java.util.concurrent.ExecutionException;

public interface IServer {
    void Start(int port) throws CertificateException, SSLException, ExecutionException, InterruptedException;

    void ShutdownNow();

    void BlockedWait();
}
