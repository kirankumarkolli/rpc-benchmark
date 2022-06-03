//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Http11Kestral
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Security;
    using System.Security.Cryptography;

    internal sealed class StringHMACSHA256Hash : IComputeHash
    {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private readonly String base64EncodedKey;
        private readonly byte[] keyBytes;
        private SecureString secureString;
        private ConcurrentQueue<HMACSHA256> hmacPool;

        public StringHMACSHA256Hash(String base64EncodedKey)
        {
            this.base64EncodedKey = base64EncodedKey;
            this.keyBytes = Convert.FromBase64String(base64EncodedKey);
            this.hmacPool = new ConcurrentQueue<HMACSHA256>();
        }

        public byte[] ComputeHash(ArraySegment<byte> bytesToHash)
        {
            if (this.hmacPool.TryDequeue(out HMACSHA256 hmacSha256))
            {
                hmacSha256.Initialize();
            }
            else
            {
                hmacSha256 = new HMACSHA256(this.keyBytes);
            }

            try
            {
                return hmacSha256.ComputeHash(bytesToHash.Array, 0, (int)bytesToHash.Count);
            }
            finally
            {
                this.hmacPool.Enqueue(hmacSha256);
            }
        }

        public void Dispose()
        {
            while (this.hmacPool.TryDequeue(out HMACSHA256 hmacsha256))
            {
                hmacsha256.Dispose();
            }

            if (this.secureString != null)
            {
                this.secureString.Dispose();
                this.secureString = null;
            }
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
    }
}
