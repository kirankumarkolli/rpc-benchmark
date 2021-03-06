package rntbd;

import com.google.common.collect.ImmutableMap;
import com.google.common.collect.ImmutableSet;
import com.google.common.collect.Maps;
import io.netty.buffer.ByteBuf;
import io.netty.handler.codec.CorruptedFrameException;
import io.netty.util.ReferenceCounted;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.stream.Collector;

import static com.google.common.base.Preconditions.checkArgument;
import static com.google.common.base.Preconditions.checkNotNull;
import static com.google.common.base.Preconditions.checkState;
import static com.google.common.base.Strings.lenientFormat;

@SuppressWarnings("UnstableApiUsage")
abstract class RntbdTokenStream<T extends Enum<T> & RntbdConstants.RntbdHeader> implements ReferenceCounted {
    private final static Logger logger = LoggerFactory.getLogger(RntbdTokenStream.class);

    final ByteBuf in;
    final ImmutableMap<Short, T> headers;
    final ImmutableMap<T, RntbdToken> tokens;

    RntbdTokenStream(final ImmutableSet<T> headers, final ImmutableMap<Short, T> ids, final ByteBuf in) {

        checkNotNull(headers, "expected non-null headers");
        checkNotNull(ids, "expected non-null ids");
        checkNotNull(in, "expected non-null in");

        final Collector<T, ?, ImmutableMap<T, RntbdToken>> collector = Maps.toImmutableEnumMap(h -> h, RntbdToken::create);
        this.tokens = headers.stream().collect(collector);
        this.headers = ids;
        this.in = in;
    }

    // region Methods

    final int computeCount() {

        int count = 0;

        for (final RntbdToken token : this.tokens.values()) {
            if (token.isPresent()) {
                ++count;
            }
        }

        return count;
    }

    final int computeLength() {

        int total = 0;

        for (final RntbdToken token : this.tokens.values()) {
            total += token.computeLength();
        }

        return total;
    }

    static <T extends RntbdTokenStream<?>> T decode(final T stream) {

        final ByteBuf in = stream.in;

        while (in.readableBytes() > 0) {

            int startPosition = in.readerIndex();
            final short id = in.readShortLE();
            final RntbdTokenType type = RntbdTokenType.fromId(in.readByte());

            RntbdToken token = stream.tokens.get(stream.headers.get(id));

            if (token == null) {
                token = RntbdToken.create(new UndefinedHeader(id, type));
            }

            token.decode(in);

            int endPosition = in.readerIndex();
            if (startPosition != endPosition) {
                logger.info("[msg-id: {}] decode({}, {}): token: {}", in.memoryAddress(), startPosition, endPosition, token);
            }
        }

        for (final RntbdToken token : stream.tokens.values()) {
            if (!token.isPresent() && token.isRequired()) {
                logger.error("[msg-id: {}] Required header not found on token stream: {}", in.memoryAddress(), token);
                // throw new CorruptedFrameException(message);
            }
        }

        return stream;
    }

    final void encode(final ByteBuf out) {
        for (final RntbdToken token : this.tokens.values()) {
            token.encode(out);
        }
    }

    final RntbdToken get(final T header) {
        return this.tokens.get(header);
    }

    @Override
    public final int refCnt() {
        return this.in.refCnt();
    }

    @Override
    public final boolean release() {
        return this.release(1);
    }

    @Override
    public final boolean release(final int count) {
        return this.in.release(count);
    }

    @Override
    public final RntbdTokenStream<T> retain() {
        return this.retain(1);
    }

    @Override
    public final RntbdTokenStream<T> retain(final int count) {
        this.in.retain(count);
        return this;
    }

    @Override
    public ReferenceCounted touch(Object hint) {
        return this;
    }

    @Override
    public ReferenceCounted touch() {
        return this;
    }

    // endregion

    // region Types

    private static final class UndefinedHeader implements RntbdConstants.RntbdHeader {

        private final short id;
        private final RntbdTokenType type;

        UndefinedHeader(final short id, final RntbdTokenType type) {
            this.id = id;
            this.type = type;
        }

        @Override
        public boolean isRequired() {
            return false;
        }

        @Override
        public short id() {
            return this.id;
        }

        @Override
        public String name() {
            return "Undefined";
        }

        @Override
        public RntbdTokenType type() {
            return this.type;
        }
    }

    // endregion
}
