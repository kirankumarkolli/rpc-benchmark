package rntbd;

import io.netty.buffer.ByteBuf;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.MessageToByteEncoder;
import io.netty.util.ReferenceCountUtil;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class RntbdEncoder extends MessageToByteEncoder<IRntbdResponse> {
    private final static Logger logger = LoggerFactory.getLogger(RntbdEncoder.class);

    @Override
    protected void encode(ChannelHandlerContext ctx, IRntbdResponse msg, ByteBuf out) {
        logger.warn("[cid: 0x{} msg-id: {}] encode", ctx.channel().id(), msg.hashCode());

        msg.encode(out);

        // HACK!!!
        if (ReferenceCountUtil.refCnt(msg) == 0) {
            ReferenceCountUtil.retain(msg);
        }
    }
}
