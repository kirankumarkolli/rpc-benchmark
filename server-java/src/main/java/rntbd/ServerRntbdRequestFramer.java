package rntbd;

import io.netty.handler.codec.LengthFieldBasedFrameDecoder;
import java.nio.ByteOrder;

public class ServerRntbdRequestFramer extends LengthFieldBasedFrameDecoder {
    public ServerRntbdRequestFramer() {
        super(ByteOrder.LITTLE_ENDIAN, Integer.MAX_VALUE, 0, Integer.BYTES, -Integer.BYTES, 0, true);
    }
}
