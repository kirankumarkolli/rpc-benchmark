package rntbd;


import com.fasterxml.jackson.annotation.JsonFilter;
import io.netty.buffer.ByteBuf;

/**
 * Methods included in this class are copied from com.azure.cosmos.implementation.directconnectivity.rntbd.RntbdRequestHeaders.
 */
@JsonFilter("RntbdToken")
final class RntbdRequestHeaders extends RntbdTokenStream<RntbdConstants.RntbdRequestHeader> {

    // region Constructors

    private RntbdRequestHeaders(ByteBuf in) {
        super(RntbdConstants.RntbdRequestHeader.set, RntbdConstants.RntbdRequestHeader.map, in);
    }

    // endregion

    // region Methods

    static RntbdRequestHeaders decode(final ByteBuf in) {
        final RntbdRequestHeaders metadata = new RntbdRequestHeaders(in);
        return RntbdRequestHeaders.decode(metadata);
    }

    // endregion
}

