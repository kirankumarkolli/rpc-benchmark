package rntbd;


import com.fasterxml.jackson.annotation.JsonFilter;
import io.netty.buffer.ByteBuf;

/**
 * Methods included in this class are copied from com.azure.cosmos.implementation.directconnectivity.rntbd.RntbdRequestHeaders.
 */
@JsonFilter("RntbdToken")
final class ServerRntbdRequestHeaders extends RntbdTokenStream<RntbdConstants.RntbdRequestHeader> {

    // region Constructors

    private ServerRntbdRequestHeaders(ByteBuf in) {
        super(RntbdConstants.RntbdRequestHeader.set, RntbdConstants.RntbdRequestHeader.map, in);
    }

    // endregion

    // region Methods

    static ServerRntbdRequestHeaders decode(final ByteBuf in) {
        final ServerRntbdRequestHeaders metadata = new ServerRntbdRequestHeaders(in);
        return ServerRntbdRequestHeaders.decode(metadata);
    }

    // endregion
}

