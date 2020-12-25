package rntbd;

import io.netty.channel.CombinedChannelDuplexHandler;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class RntbdCodec extends CombinedChannelDuplexHandler<RntbdRequestDecoder, RntbdEncoder> {
    private final static Logger logger = LoggerFactory.getLogger(EchoRntbdRequestProcessor.class);

    public RntbdCodec() {
        super(new RntbdRequestDecoder(), new RntbdEncoder());
    }
}

