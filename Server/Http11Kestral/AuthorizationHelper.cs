using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Http11Kestral
{
    public static class AuthorizationHelper
    {
        public static string XDateHeader()
        {
            return DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
        }

        public static string GenerateAuthorizationTokenWithHashCore(
            string verb,
            string resourceId,
            string resourceType,
            string xDate,
            IComputeHash stringHMACSHA256Helper,
            bool urlEncode,
            out ArrayOwner payload)
        {
            if (resourceType == null)
            {
                throw new ArgumentNullException(nameof(resourceType)); // can be empty
            }

            if (stringHMACSHA256Helper == null)
            {
                throw new ArgumentNullException(nameof(stringHMACSHA256Helper));
            }

            // Order of the values included in the message payload is a protocol that clients/BE need to follow exactly.
            // More headers can be added in the future.
            // If any of the value is optional, it should still have the placeholder value of ""
            // OperationType -> ResourceType -> ResourceId/OwnerId -> XDate -> Date
            string verbInput = verb ?? string.Empty;
            string resourceIdInput = resourceId ?? string.Empty;
            string resourceTypeInput = resourceType ?? string.Empty;

            // AuthorizationHelper.GetAuthorizationResourceIdOrFullName(resourceTypeInput, resourceIdInput);
            // For name based its the oringinal resourceId
            string authResourceId = resourceIdInput; 

            int capacity = AuthorizationHelper.ComputeMemoryCapacity(verbInput, authResourceId, resourceTypeInput);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                Span<byte> payloadBytes = buffer;
                int length = AuthorizationHelper.SerializeMessagePayload(
                    payloadBytes,
                    verbInput,
                    authResourceId,
                    resourceTypeInput,
                    xDate);

                payload = new ArrayOwner(ArrayPool<byte>.Shared, new ArraySegment<byte>(buffer, 0, length));
                byte[] hashPayLoad = stringHMACSHA256Helper.ComputeHash(payload.Buffer);
                return AuthorizationHelper.OptimizedConvertToBase64string(hashPayLoad, urlEncode);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        /// <summary>
        /// This an optimized version of doing Convert.ToBase64String(hashPayLoad) with an optional wrapping HttpUtility.UrlEncode.
        /// This avoids the over head of converting it to a string and back to a byte[].
        /// </summary>
        private static unsafe string OptimizedConvertToBase64string(byte[] hashPayLoad, bool urlEncode)
        {
            // Create a large enough buffer that URL encode can use it.
            // Increase the buffer by 3x so it can be used for the URL encoding
            int capacity = Base64.GetMaxEncodedToUtf8Length(hashPayLoad.Length) * 3;
            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(capacity);

            try
            {
                Span<byte> encodingBuffer = rentedBuffer;
                // This replaces the Convert.ToBase64String
                OperationStatus status = Base64.EncodeToUtf8(
                    hashPayLoad,
                    encodingBuffer,
                    out int _,
                    out int bytesWritten);

                if (status != OperationStatus.Done)
                {
                    throw new ArgumentException($"Authorization key payload is invalid. {status}");
                }

                return urlEncode
                    ? AuthorizationHelper.UrlEncodeBase64SpanInPlace(encodingBuffer, bytesWritten)
                    : Encoding.UTF8.GetString(encodingBuffer.Slice(0, bytesWritten));
            }
            finally
            {
                if (rentedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }

        /// <summary>
        /// This does HttpUtility.UrlEncode functionality with Span buffer. It does an in place update to avoid
        /// creating the new buffer.
        /// </summary>
        /// <param name="base64Bytes">The buffer that include the bytes to url encode.</param>
        /// <param name="length">The length of bytes used in the buffer</param>
        /// <returns>The URLEncoded string of the bytes in the buffer</returns>
        private unsafe static string UrlEncodeBase64SpanInPlace(Span<byte> base64Bytes, int length)
        {
            if (base64Bytes == default)
            {
                throw new ArgumentNullException(nameof(base64Bytes));
            }

            if (base64Bytes.Length < length * 3)
            {
                throw new ArgumentException($"{nameof(base64Bytes)} should be 3x to avoid running out of space in worst case scenario where all characters are special");
            }

            if (length == 0)
            {
                return string.Empty;
            }

            int escapeBufferPosition = base64Bytes.Length - 1;
            for (int i = length - 1; i >= 0; i--)
            {
                byte curr = base64Bytes[i];
                // Base64 is limited to Alphanumeric characters and '/' '=' '+'
                switch (curr)
                {
                    case (byte)'/':
                        base64Bytes[escapeBufferPosition--] = (byte)'f';
                        base64Bytes[escapeBufferPosition--] = (byte)'2';
                        base64Bytes[escapeBufferPosition--] = (byte)'%';
                        break;
                    case (byte)'=':
                        base64Bytes[escapeBufferPosition--] = (byte)'d';
                        base64Bytes[escapeBufferPosition--] = (byte)'3';
                        base64Bytes[escapeBufferPosition--] = (byte)'%';
                        break;
                    case (byte)'+':
                        base64Bytes[escapeBufferPosition--] = (byte)'b';
                        base64Bytes[escapeBufferPosition--] = (byte)'2';
                        base64Bytes[escapeBufferPosition--] = (byte)'%';
                        break;
                    default:
                        base64Bytes[escapeBufferPosition--] = curr;
                        break;
                }
            }

            Span<byte> endSlice = base64Bytes.Slice(escapeBufferPosition + 1);
            fixed (byte* bp = endSlice)
            {
                return Encoding.UTF8.GetString(bp, endSlice.Length);
            }
        }

        // This function is used by Compute
        internal static int ComputeMemoryCapacity(string verbInput, string authResourceId, string resourceTypeInput)
        {
            return
                verbInput.Length
                + AuthorizationHelper.AuthorizationEncoding.GetMaxByteCount(authResourceId.Length)
                + resourceTypeInput.Length
                + 5 // new line characters
                + 30; // date header length;
        }

        private static int SerializeMessagePayload(
               Span<byte> stream,
               string verb,
               string resourceId,
               string resourceType,
               string xDate)
        {
            int totalLength = 0;
            int length = stream.Write(verb.ToLowerInvariant());
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write("\n");
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write(resourceType.ToLowerInvariant());
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write("\n");
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write(resourceId);
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write("\n");
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write(xDate.ToLowerInvariant());
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write("\n");
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write(xDate.Equals(string.Empty, StringComparison.OrdinalIgnoreCase) ? xDate.ToLowerInvariant() : string.Empty);
            totalLength += length;
            stream = stream.Slice(length);
            length = stream.Write("\n");
            totalLength += length;
            return totalLength;
        }

        private static int Write(this Span<byte> stream, string contentToWrite)
        {
            int actualByteCount = AuthorizationHelper.AuthorizationEncoding.GetBytes(
                contentToWrite,
                stream);
            return actualByteCount;
        }

        private static readonly Encoding AuthorizationEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public struct ArrayOwner : IDisposable
        {
            private readonly ArrayPool<byte> pool;

            public ArrayOwner(ArrayPool<byte> pool, ArraySegment<byte> buffer)
            {
                this.pool = pool;
                this.Buffer = buffer;
            }

            public ArraySegment<byte> Buffer { get; private set; }

            public void Dispose()
            {
                if (this.Buffer.Array != null)
                {
                    this.pool?.Return(this.Buffer.Array);
                    this.Buffer = default;
                }
            }
        }
    }
}
