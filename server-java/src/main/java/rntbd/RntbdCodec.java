package rntbd;

import io.netty.channel.CombinedChannelDuplexHandler;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class RntbdCodec extends CombinedChannelDuplexHandler<ServerRntbdRequestDecoder, ServerRntbdContextEncoder> {
    private final static Logger logger = LoggerFactory.getLogger(ServerRntbdRequestProcessor.class);

    public RntbdCodec() {
        super(new ServerRntbdRequestDecoder(), new ServerRntbdContextEncoder());
    }
}

