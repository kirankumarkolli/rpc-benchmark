// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package rntbd;

import io.netty.buffer.ByteBuf;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.ByteToMessageDecoder;
import io.netty.util.ReferenceCountUtil;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.List;

public final class ServerRntbdRequestDecoder extends ByteToMessageDecoder {
    private final static Logger logger = LoggerFactory.getLogger(ServerRntbdRequestDecoder.class);

//    @Override
//    public void channelRead(final ChannelHandlerContext context, final Object message) throws Exception {
//
//        logger.info("Got channelRead with ctx: {} for type {} Hashcode: {}", context.name(), message.getClass().getName(), message.hashCode());
//
//        boolean channelReadFired = false;
//        try {
//
//            if (message instanceof ByteBuf) {
//
//                final ByteBuf in = (ByteBuf) message;
//
//                final int resourceTypeInt = in.getUnsignedShort(in.readerIndex() + Integer.BYTES);
//                final int operationTypeInt = in.getUnsignedShort(in.readerIndex() + Integer.BYTES + 2); // UINT (resource type)
//                logger.info("channelRead resourceType: {} operatioType: {}", resourceTypeInt, operationTypeInt);
//
//                boolean supportType = false;
//                switch (resourceTypeInt) {
//                    case 0:
//                        if (operationTypeInt == 0) {
//                            supportType = true;
//                        }
//                        break;
//                    case 2:
//                        if (operationTypeInt == 3) {
//                            supportType = true;
//                        }
//                        break;
//                }
//
//                if (!supportType) {
//                    Throwable ex = new Exception("Unsupported resourceType {" + resourceTypeInt + "} OR operationType" + operationTypeInt);
//                    context.fireExceptionCaught(ex);
//                }
//            }
//
//            logger.info("Firing fireChannelRead for message: {} with hashcode: {}", message.getClass().getName(), message.hashCode());
//            context.fireChannelRead(message);
//            channelReadFired = true;
//        }
//        finally {
//            if (!channelReadFired) {
//                ReferenceCountUtil.release(message);
//            }
//        }
//    }

    @Override
    protected void decode(
            final ChannelHandlerContext context,
            final ByteBuf in,
            final List<Object> out) throws Exception {

        logger.warn("[cid: 0x{} msg-id: {}] ", context.channel().id(), in.memoryAddress());

        final ServerRntbdRequest request = ServerRntbdRequest.decode(in);
        logger.info("[cid: 0x{} msg-id: {}] channelRead resourceType: {} operationType: {} decoded-msg-id: {}", context.channel().id(), in.memoryAddress(), request.resourceTypeInt, request.operationTypeInt, request.hashCode());

        out.add(request);
    }
}