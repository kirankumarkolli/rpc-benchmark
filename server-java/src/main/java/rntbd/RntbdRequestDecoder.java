// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package rntbd;

import io.netty.buffer.ByteBuf;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.ByteToMessageDecoder;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.List;

public final class RntbdRequestDecoder extends ByteToMessageDecoder {
    private final static Logger logger = LoggerFactory.getLogger(RntbdRequestDecoder.class);

    @Override
    protected void decode(
            final ChannelHandlerContext context,
            final ByteBuf in,
            final List<Object> out) throws Exception {

        logger.info("[cid: 0x{} msg-id: {}] ", context.channel().id(), in.memoryAddress());

        final RntbdRequest request = RntbdRequest.decode(in);
        logger.info("[cid: 0x{} msg-id: {}] channelRead resourceType: {} operationType: {} decoded-msg-id: {}", context.channel().id(), in.memoryAddress(), request.resourceTypeInt, request.operationTypeInt, request.hashCode());

        out.add(request);
    }
}