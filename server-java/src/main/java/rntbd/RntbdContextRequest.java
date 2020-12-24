package rntbd;

import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectWriter;
import com.google.common.base.Strings;
import io.netty.buffer.ByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.handler.codec.CorruptedFrameException;

import java.nio.charset.StandardCharsets;
import java.util.UUID;

public final class RntbdContextRequest {

    @JsonProperty
    private final UUID activityId;

    @JsonProperty
    private final Headers headers;

    RntbdContextRequest(final UUID activityId, final String userAgent) {
        this(activityId, new Headers(userAgent));
    }

    private RntbdContextRequest(final UUID activityId, final Headers headers) {
        this.activityId = activityId;
        this.headers = headers;
    }

    public UUID getActivityId() {
        return this.activityId;
    }

    public String getClientVersion() {
        return this.headers.clientVersion.getValue(String.class);
    }

    public static RntbdContextRequest decode(final ByteBuf in) {

        final int resourceOperationTypeCode = in.getInt(in.readerIndex() + Integer.BYTES);

        if (resourceOperationTypeCode != 0) {
            final String reason = String.format("resourceOperationCode=0x%08X", resourceOperationTypeCode);
            throw new IllegalStateException(reason);
        }

        final int start = in.readerIndex();
        final int expectedLength = in.readIntLE();

        final RntbdRequestFrame header = RntbdRequestFrame.decode(in);
        final Headers headers = Headers.decode(in.readSlice(expectedLength - (in.readerIndex() - start)));

        final int observedLength = in.readerIndex() - start;

        if (observedLength != expectedLength) {
            final String reason = Strings.lenientFormat("expectedLength=%s, observedLength=%s", expectedLength, observedLength);
            throw new IllegalStateException(reason);
        }

        in.discardReadBytes();
        return new RntbdContextRequest(header.getActivityId(), headers);
    }

    public void encode(final ByteBuf out) {

        final int expectedLength = RntbdRequestFrame.LENGTH + this.headers.computeLength();
        final int start = out.writerIndex();

        out.writeIntLE(expectedLength);

        final RntbdRequestFrame header = new RntbdRequestFrame(this.getActivityId(), RntbdConstants.RntbdOperationType.Connection, RntbdConstants.RntbdResourceType.Connection);
        header.encode(out);
        this.headers.encode(out);

        final int observedLength = out.writerIndex() - start;

        if (observedLength != expectedLength) {
            final String reason = Strings.lenientFormat("expectedLength=%s, observedLength=%s", expectedLength, observedLength);
            throw new IllegalStateException(reason);
        }
    }

    @Override
    public String toString() {
        final ObjectWriter writer = ServerRntbdObjectMapper.writer();
        try {
            return writer.writeValueAsString(this);
        } catch (final JsonProcessingException error) {
            throw new CorruptedFrameException(error);
        }
    }

    private static final class Headers extends RntbdTokenStream<RntbdConstants.RntbdContextRequestHeader> {

        private static final byte[] ClientVersion = HttpConstants.Versions.CURRENT_VERSION.getBytes(StandardCharsets.UTF_8);

        @JsonProperty
        RntbdToken clientVersion;

        @JsonProperty
        RntbdToken protocolVersion;

        @JsonProperty
        RntbdToken userAgent;

        Headers(final String userAgent) {
            this(Unpooled.EMPTY_BUFFER);
            this.clientVersion.setValue(ClientVersion);
            this.userAgent.setValue(userAgent);
            this.protocolVersion.setValue(RntbdConstants.CURRENT_PROTOCOL_VERSION);
        }

        private Headers(ByteBuf in) {

            super(RntbdConstants.RntbdContextRequestHeader.set, RntbdConstants.RntbdContextRequestHeader.map, in);

            this.clientVersion = this.get(RntbdConstants.RntbdContextRequestHeader.ClientVersion);
            this.protocolVersion = this.get(RntbdConstants.RntbdContextRequestHeader.ProtocolVersion);
            this.userAgent = this.get(RntbdConstants.RntbdContextRequestHeader.UserAgent);
        }

        static Headers decode(final ByteBuf in) {
            return Headers.decode(new Headers(in));
        }
    }
}
