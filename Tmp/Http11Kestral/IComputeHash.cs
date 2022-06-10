//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Http11Kestral
{
    using System;
    using System.IO;
    using System.Security;

    public interface IComputeHash : IDisposable
    {
        byte[] ComputeHash(ArraySegment<byte> bytesToHash);
    }
}
