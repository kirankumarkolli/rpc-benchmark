import reactor.netty.DisposableServer;

public abstract class EchoServerBase implements IServer {
    protected DisposableServer server;

    @Override
    public void ShutdownNow() {
        server.disposeNow();
    }

    @Override
    public void BlockedWait() {
        server
                .onDispose()
                .block();
    }
}
