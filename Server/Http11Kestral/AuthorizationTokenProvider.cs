//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Http11Kestral
{
    using System;
    using System.Threading.Tasks;

    public abstract class AuthorizationTokenProvider : IDisposable
    {
        public abstract string DocumentReadAuthorizationToken(
            string resourceId,
            string xDate,
            IComputeHash stringHMACSHA256Helper);

        public abstract string GetUserAuthorizationAsync(
            string resourceAddress,
            string resourceType,
            string requestVerb,
            string xDate);

        public abstract void Dispose();
    }
}
