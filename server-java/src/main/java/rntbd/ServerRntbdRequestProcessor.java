package rntbd;

import io.netty.buffer.ByteBuf;
import io.netty.buffer.EmptyByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.channel.*;
import io.netty.handler.codec.http.HttpResponseStatus;
import io.netty.util.ReferenceCountUtil;
import io.netty.util.ReferenceCounted;
import io.netty.util.concurrent.ImmediateEventExecutor;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.math.BigInteger;
import java.nio.charset.Charset;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/**
 * Processing incoming requests.
 */
public final class ServerRntbdRequestProcessor extends ChannelDuplexHandler {
    private final static Logger logger = LoggerFactory.getLogger(ServerRntbdRequestProcessor.class);

    public ServerRntbdRequestProcessor() {
        super();
    }

    @Override
    public void channelRead(final ChannelHandlerContext context, final Object message) throws Exception {
        if (!(message instanceof ServerRntbdRequest)) {
            ByteBuf tryByteBuf = (ByteBuf) message;
            long hashCode = (tryByteBuf == null) ? message.hashCode() : tryByteBuf.memoryAddress();
            throw new Exception(String.format("Unexpeted message [msg-id: {}] type: {}", hashCode, message.getClass().getName()));
        }

        IRntbdResponse response = null;
        ServerRntbdRequest request = (ServerRntbdRequest) message;
        logger.warn("[cid: 0x{} msg-id: {}] channelRead msg: {}", context.channel().id(), message.hashCode(), message);

        if (request.resourceTypeInt == 0 && request.operationTypeInt == 0) { // connection-Connect
            response = new RntbdContext(
                    request.activityId,
                    HttpResponseStatus.OK,
                    "",
                    120L,
                    0,
                    25L,
                    "RntbdMockServer",
                    "1.0");
        } else if(request.resourceTypeInt == 3 && request.operationTypeInt == 3) { // Document-Read
            ByteBuf payload = Unpooled.copiedBuffer(Utils.testJsonPayload, Charset.defaultCharset());

            Map<String, String> map = new HashMap<>();
            map.put(HttpConstants.HttpHeaders.CONTENT_TYPE, "application/json");
            map.put(HttpConstants.HttpHeaders.REQUEST_CHARGE, "1.0");
            map.put(HttpConstants.HttpHeaders.CONTENT_LENGTH, Integer.toString(payload.writerIndex()));
            map.put(HttpConstants.HttpHeaders.TRANSPORT_REQUEST_ID, String.valueOf(request.transportRequestId));

            response = new RntbdResponse(
                    request.activityId,
                    200,
                    map,
                    payload);
        }

        if (response == null) {
            Map<String, String> map = new HashMap<>();
            map.put(HttpConstants.HttpHeaders.CONTENT_TYPE, "application/json");
            map.put(HttpConstants.HttpHeaders.REQUEST_CHARGE, "1.0");
            map.put(HttpConstants.HttpHeaders.TRANSPORT_REQUEST_ID, String.valueOf(request.transportRequestId));

            response = new RntbdResponse(
                    request.activityId,
                    500,
                    map,
                    Unpooled.EMPTY_BUFFER);
        }

        logger.warn("[cid: 0x{} msg-id: {}] created new msg: {}", context.channel().id(), message.hashCode(), response.hashCode());

        // ReferenceCountUtil.safeRelease(message);
        context.fireChannelReadComplete();
        ChannelFuture future = context.writeAndFlush(response);
    }

    @Override
    @SuppressWarnings("deprecation")
    public void exceptionCaught(ChannelHandlerContext ctx, Throwable cause)
            throws Exception {
        logger.error("[cid: 0x{}] exceptionCaught cause: {}", ctx.channel().id(), cause.toString());

        ctx.fireExceptionCaught(cause);
    }

    @Override
    public void userEventTriggered(ChannelHandlerContext ctx, Object evt) {

        logger.info("[cid: 0x{}] userEventTriggered evt-id: {} evt: {}", ctx.channel().id(), evt.hashCode(), evt.toString());
        ctx.fireUserEventTriggered(evt);
    }
}
