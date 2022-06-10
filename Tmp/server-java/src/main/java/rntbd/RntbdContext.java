package rntbd;


import com.fasterxml.jackson.annotation.JsonIgnore;
import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.databind.node.ObjectNode;
import io.netty.buffer.ByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.handler.codec.http.HttpResponseStatus;

import java.util.Collections;
import java.util.HashMap;
import java.util.UUID;

import static com.google.common.base.Preconditions.checkState;

public final class RntbdContext implements IRntbdResponse {

    private final UUID activityId;
    private final HttpResponseStatus status;
    private final String clientVersion;
    private final long idleTimeoutInSeconds;
    private final int protocolVersion;
    private final ServerProperties serverProperties;
    private final long unauthenticatedTimeoutInSeconds;

    public RntbdContext(
            final UUID activityId,
            final HttpResponseStatus status,
            final String clientVersion,
            final long idleTimeoutInSeconds,
            final int protocolVersion,
            final long unauthenticatedTimeoutInSeconds,
            final String serverAgent,
            final String serverVersion) {
        this.activityId = activityId;
        this.status = status;
        this.clientVersion = clientVersion;
        this.idleTimeoutInSeconds = idleTimeoutInSeconds;
        this.protocolVersion = protocolVersion;
        this.unauthenticatedTimeoutInSeconds = unauthenticatedTimeoutInSeconds;
        this.serverProperties = new ServerProperties(serverAgent, serverVersion);

    }

    @JsonProperty
    public UUID activityId() {
        return this.activityId;
    }

    @JsonProperty
    public String clientVersion() {
        return this.clientVersion;
    }

    @JsonProperty
    public long idleTimeoutInSeconds() {
        return this.idleTimeoutInSeconds;
    }

    @JsonProperty
    public int protocolVersion() {
        return this.protocolVersion;
    }

    @JsonProperty
    public ServerProperties serverProperties() {
        return this.serverProperties;
    }

    @JsonIgnore
    public String serverVersion() {
        return this.serverProperties.getVersion();
    }

    @JsonIgnore
    public HttpResponseStatus status() {
        return this.status;
    }

    @JsonProperty
    public int getStatusCode() {
        return this.status.code();
    }

    @JsonProperty
    public long getUnauthenticatedTimeoutInSeconds() {
        return this.unauthenticatedTimeoutInSeconds;
    }

    @Override
    public void encode(final ByteBuf out) {

        final Headers headers = new Headers(this);
        final int length = RntbdResponseStatus.LENGTH + headers.computeLength();
        final RntbdResponseStatus responseStatus = new RntbdResponseStatus(length, this.status(), this.activityId());

        final int start = out.writerIndex();

        responseStatus.encode(out);
        headers.encode(out);
        headers.release();

        final int end = out.writerIndex();

        checkState(end - start == responseStatus.getLength());
    }

    @Override
    public String toString() {
        return ServerRntbdObjectMapper.toString(this);
    }

    private static final class Headers extends RntbdTokenStream<RntbdConstants.RntbdContextHeader> {

        final RntbdToken clientVersion;
        final RntbdToken idleTimeoutInSeconds;
        final RntbdToken protocolVersion;
        final RntbdToken serverAgent;
        final RntbdToken serverVersion;
        final RntbdToken unauthenticatedTimeoutInSeconds;

        private Headers(final RntbdContext context) {
            this(Unpooled.EMPTY_BUFFER);
            this.clientVersion.setValue(context.clientVersion());
            this.idleTimeoutInSeconds.setValue(context.idleTimeoutInSeconds());
            this.protocolVersion.setValue(context.protocolVersion());
            this.serverAgent.setValue(context.serverProperties().getAgent());
            this.serverVersion.setValue(context.serverProperties().getVersion());
            this.unauthenticatedTimeoutInSeconds.setValue(context.unauthenticatedTimeoutInSeconds);
        }

        Headers(final ByteBuf in) {
            super(RntbdConstants.RntbdContextHeader.set, RntbdConstants.RntbdContextHeader.map, in);
            this.clientVersion = this.get(RntbdConstants.RntbdContextHeader.ClientVersion);
            this.idleTimeoutInSeconds = this.get(RntbdConstants.RntbdContextHeader.IdleTimeoutInSeconds);
            this.protocolVersion = this.get(RntbdConstants.RntbdContextHeader.ProtocolVersion);
            this.serverAgent = this.get(RntbdConstants.RntbdContextHeader.ServerAgent);
            this.serverVersion = this.get(RntbdConstants.RntbdContextHeader.ServerVersion);
            this.unauthenticatedTimeoutInSeconds = this.get(RntbdConstants.RntbdContextHeader.UnauthenticatedTimeoutInSeconds);
        }
    }
}
