package rntbd;

import io.netty.buffer.ByteBuf;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.LengthFieldBasedFrameDecoder;
import java.nio.ByteOrder;
import java.util.List;

public class RntbdRequestFrameInAdapter extends LengthFieldBasedFrameDecoder {
    public RntbdRequestFrameInAdapter() {
        super(ByteOrder.LITTLE_ENDIAN,
                4*1024*1024, //Integer.MAX_VALUE,
                0,
                Integer.BYTES,
                -4,
                0, // don't strip the length header
                true);
    }
}
