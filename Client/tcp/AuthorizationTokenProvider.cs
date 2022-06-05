//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Collections;

    internal abstract class AuthorizationTokenProvider : IAuthorizationTokenProvider, IDisposable
    {
        public async Task AddSystemAuthorizationHeaderAsync(
            DocumentServiceRequest request, 
            string federationId, 
            string verb, 
            string resourceId)
        {
            request.Headers[HttpConstants.HttpHeaders.XDate] = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);

            request.Headers[HttpConstants.HttpHeaders.Authorization] = (await this.GetUserAuthorizationAsync(
                resourceId ?? request.ResourceAddress,
                PathsHelper.GetResourcePath(request.ResourceType),
                verb,
                request.Headers,
                request.RequestAuthorizationTokenType)).token;
        }

        public abstract ValueTask AddAuthorizationHeaderAsync(
            INameValueCollection headersCollection,
            Uri requestAddress,
            string verb,
            AuthorizationTokenType tokenType);

        public abstract ValueTask<(string token, string payload)> GetUserAuthorizationAsync(
            string resourceAddress,
            string resourceType,
            string requestVerb,
            INameValueCollection headers,
            AuthorizationTokenType tokenType);

        public abstract void TraceUnauthorized(
            DocumentClientException dce,
            string authorizationToken,
            string payload);

        //public static AuthorizationTokenProvider CreateWithResourceTokenOrAuthKey(string authKeyOrResourceToken)
        //{
        //    if (string.IsNullOrEmpty(authKeyOrResourceToken))
        //    {
        //        throw new ArgumentNullException(nameof(authKeyOrResourceToken));
        //    }

        //    if (AuthorizationHelper.IsResourceToken(authKeyOrResourceToken))
        //    {
        //        //return new AuthorizationTokenProviderResourceToken(authKeyOrResourceToken);
        //        throw new NotImplementedException();
        //    }
        //    else
        //    {
        //        return new AuthorizationTokenProviderMasterKey(authKeyOrResourceToken);
        //    }
        //}

        public abstract void Dispose();
    }
}
