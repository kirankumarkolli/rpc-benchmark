// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package rntbd;

import io.netty.buffer.ByteBuf;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.ByteToMessageDecoder;

import java.util.List;

public final class ServerRntbdRequestDecoder extends ByteToMessageDecoder {
    @Override
    public void channelRead(final ChannelHandlerContext context, final Object message) throws Exception {

        if (message instanceof ByteBuf) {

            final ByteBuf in = (ByteBuf) message;
            final int resourceOperationType = in.getInt(in.readerIndex() + Integer.BYTES);

            if (resourceOperationType != 0) {
                super.channelRead(context, message);
                return;
            }
        }

        context.fireChannelRead(message);
    }

    @Override
    protected void decode(
            final ChannelHandlerContext context,
            final ByteBuf in,
            final List<Object> out) throws IllegalStateException {

        final ServerRntbdRequest request;
        in.markReaderIndex();

        try {
            request = ServerRntbdRequest.decode(in);
            if(request!= null) {
                in.discardReadBytes();
                out.add(request);
            } else {
                in.resetReaderIndex();
                return;
            }
        } catch (final IllegalStateException error) {
            in.resetReaderIndex();
            throw error;
        }
    }
}