import javax.net.ssl.SSLException;
import java.security.cert.CertificateException;

public interface IServer {
    void Start(int port) throws CertificateException, SSLException;

    void ShutdownNow();

    void BlockedWait();
}
