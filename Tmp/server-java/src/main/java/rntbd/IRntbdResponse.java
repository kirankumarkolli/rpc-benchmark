package rntbd;

import io.netty.buffer.ByteBuf;

public interface IRntbdResponse {
    void encode(final ByteBuf out);
}
