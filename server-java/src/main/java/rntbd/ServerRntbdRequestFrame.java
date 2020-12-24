package rntbd;

import io.netty.buffer.ByteBuf;

import java.util.UUID;

/**
 * Methods included in this class are copied from com.azure.cosmos.implementation.directconnectivity.rntbd.RntbdRequestFrame.
 */
final class ServerRntbdRequestFrame {
    // region Fields

    private final UUID activityId;
    private final RntbdConstants.RntbdOperationType operationType;
    private final RntbdConstants.RntbdResourceType resourceType;

    // region Constructors

    ServerRntbdRequestFrame(final UUID activityId, final RntbdConstants.RntbdOperationType operationType, final RntbdConstants.RntbdResourceType resourceType) {
        this.activityId = activityId;
        this.operationType = operationType;
        this.resourceType = resourceType;
    }

    // endregion

    // region Methods

    UUID getActivityId() {
        return this.activityId;
    }

    RntbdConstants.RntbdOperationType getOperationType() {
        return this.operationType;
    }

    static ServerRntbdRequestFrame decode(final ByteBuf in) {

        final RntbdConstants.RntbdResourceType resourceType = RntbdConstants.RntbdResourceType.fromId(in.readShortLE());
        final RntbdConstants.RntbdOperationType operationType = RntbdConstants.RntbdOperationType.fromId(in.readShortLE());
        final UUID activityId = RntbdUUID.decode(in);

        return new ServerRntbdRequestFrame(activityId, operationType, resourceType);
    }

    // endregion
}

