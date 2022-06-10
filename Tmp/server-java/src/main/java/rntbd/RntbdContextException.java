package rntbd;

import com.fasterxml.jackson.databind.JsonSerializable;
import com.fasterxml.jackson.databind.node.ObjectNode;
import io.netty.handler.codec.http.HttpResponseStatus;

import java.util.Map;

public final class RntbdContextException extends TransportException {

    final private CosmosError cosmosError;
    final private Map<String, Object> responseHeaders;
    final private HttpResponseStatus status;

    RntbdContextException(HttpResponseStatus status, ObjectNode details, Map<String, Object> responseHeaders) {

        super(status + ": " + details, null);

        this.cosmosError = new CosmosError(details);
        this.responseHeaders = responseHeaders;
        this.status = status;
    }

    public CosmosError getCosmosError() {
        return cosmosError;
    }

    public Map<String, Object> getResponseHeaders() {
        return responseHeaders;
    }

    public HttpResponseStatus getStatus() {
        return status;
    }
}

class TransportException extends RuntimeException {
    public TransportException(String message, Throwable cause) {
        super(message, cause, /* enableSuppression */ true, /* writableStackTrace */ false);
    }
}

class CosmosError {

    /**
     * Initialize a new instance of the Error object.
     */
    public CosmosError() {
        super();
    }

    /**
     * Initialize a new instance of the Error object from a JSON string.
     *
     * @param objectNode the {@link ObjectNode} that represents the error.
     */
    public CosmosError(ObjectNode objectNode) {
    } //super(objectNode);

    /**
     * Initialize a new instance of the Error object from a JSON string.
     *
     * @param jsonString the jsonString that represents the error.
     */
    public CosmosError(String jsonString) {
        //super(jsonString);
    }

    /**
     * Initialize a new instance of the Error object.
     *
     * @param errorCode the error code.
     * @param message the error message.
     */
    public CosmosError(String errorCode, String message) {
        this(errorCode, message, null);
    }

    /**
     * Initialize a new instance of the Error object.
     *
     * @param errorCode the error code.
     * @param message the error message.
     * @param additionalErrorInfo additional error info.
     */
    public CosmosError(String errorCode, String message, String additionalErrorInfo) {
        super();
//        this.setCode(errorCode);
//        this.setMessage(message);
//        this.setAdditionalErrorInfo(additionalErrorInfo);
    }
//
//    /**
//     * Gets the error code.
//     *
//     * @return the error code.
//     */
//    public String getCode() {
//        return super.getString(Constants.Properties.CODE);
//    }
//
//    /**
//     * Sets the error code.
//     *
//     * @param code the error code.
//     */
//    private void setCode(String code) {
//        super.set(Constants.Properties.CODE, code);
//    }
//
//    /**
//     * Gets the error message.
//     *
//     * @return the error message.
//     */
//    public String getMessage() {
//        return super.getString(Constants.Properties.MESSAGE);
//    }
//
//    /**
//     * Sets the error message.
//     *
//     * @param message the error message.
//     */
//    private void setMessage(String message) {
//        super.set(Constants.Properties.MESSAGE, message);
//    }
//
//    /**
//     * Gets the error details.
//     *
//     * @return the error details.
//     */
//    public String getErrorDetails() {
//        return super.getString(Constants.Properties.ERROR_DETAILS);
//    }
//
//    /**
//     * Sets the partitioned query execution info.
//     *
//     * @param additionalErrorInfo the partitioned query execution info.
//     */
//    private void setAdditionalErrorInfo(String additionalErrorInfo) {
//        super.set(Constants.Properties.ADDITIONAL_ERROR_INFO, additionalErrorInfo);
//    }
//
//    /**
//     * Gets the partitioned query execution info.
//     *
//     * @return the partitioned query execution info.
//     */
//    public String getPartitionedQueryExecutionInfo() {
//        return super.getString(Constants.Properties.ADDITIONAL_ERROR_INFO);
//    }
}
