// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package rntbd;

import io.netty.buffer.ByteBuf;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.UUID;

public final class ServerRntbdRequest {
    private final static Logger logger = LoggerFactory.getLogger(ServerRntbdRequest.class);

    public final int resourceTypeInt;
    public final int operationTypeInt;
    public final UUID activityId;
    public final long transportRequestId;

    private ServerRntbdRequest(final int resourceType,
                               final int operationType,
                               final UUID activityId,
                               final long transactionalId) {

        this.resourceTypeInt = resourceType;
        this.operationTypeInt = operationType;
        this.activityId = activityId;
        this.transportRequestId = transactionalId;
    }

    public static ServerRntbdRequest decode(final ByteBuf in) {
        final int length = in.getIntLE(in.readerIndex());
        final int resourceTypeInt = in.getUnsignedShortLE(in.readerIndex() + Integer.BYTES);
        final int operationTypeInt = in.getUnsignedShortLE(in.readerIndex() + Integer.BYTES + 2); // UINT (resource type)

        final int activityIdPos = in.readerIndex() + 8;
        in.readerIndex(activityIdPos);
        UUID activityUuid = RntbdUUID.decode(in);
        logger.info("[msg-id: {}] ServerRntbdRequest.decode length: {} resourceType: {} operationType: {} activityId: {}", in.memoryAddress(), length, resourceTypeInt, operationTypeInt, activityUuid);

        long transactionalId = 0; // not-defined
        if (resourceTypeInt != 0) {
            // HACK: RntbdToken types are overloaded (ex: ProtocolVersion in connect vs payloadpresent in request)
            in.readerIndex(activityIdPos + 16);
            ServerRntbdRequestHeaders requestHeaders = ServerRntbdRequestHeaders.decode(in);
            transactionalId = (long)requestHeaders.get(RntbdConstants.RntbdRequestHeader.TransportRequestID).getValue();
        }

        in.readerIndex(length);
        in.discardReadBytes();
        return new ServerRntbdRequest(resourceTypeInt, operationTypeInt, activityUuid, transactionalId);
    }
}