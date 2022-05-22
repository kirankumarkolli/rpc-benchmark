//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Http11Kestral
{
    using System;
    using System.Globalization;
    using System.Net;
    using System.Security;
    using System.Text;
    using System.Threading.Tasks;

    public sealed class AuthorizationTokenProviderMasterKey : AuthorizationTokenProvider
    {
        private static string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

        public static AuthorizationTokenProvider NewEmulatorAuthProvider()
        {
            return new AuthorizationTokenProviderMasterKey(AuthorizationTokenProviderMasterKey.EmulatorKey);
        }

        ////The MAC signature found in the HTTP request is not the same as the computed signature.Server used following string to sign
        ////The input authorization token can't serve the request. Please check that the expected payload is built as per the protocol, and check the key being used. Server used the following payload to sign
        private readonly IComputeHash authKeyHashFunction;
        private bool isDisposed = false;

        public AuthorizationTokenProviderMasterKey(IComputeHash computeHash)
        {
            this.authKeyHashFunction = computeHash ?? throw new ArgumentNullException(nameof(computeHash));
        }

        public AuthorizationTokenProviderMasterKey(string authKey)
            : this(new StringHMACSHA256Hash(authKey))
        {
        }

        public override string DocumentReadAuthorizationToken(
            string resourceId,
            string xDate)
        {
            return this.GetUserAuthorizationAsync(
                resourceId,
                "docs",
                "get",
                xDate
                );
        }


        public override string GetUserAuthorizationAsync(
            string resourceAddress,
            string resourceType,
            string requestVerb,
            string xDate)
        {
            string authorizationToken = AuthorizationHelper.GenerateAuthorizationTokenWithHashCore(
                requestVerb,
                resourceAddress,
                resourceType,
                xDate,
                this.authKeyHashFunction,
                urlEncode: true,
                out AuthorizationHelper.ArrayOwner arrayOwner);

            using (arrayOwner)
            {
                return authorizationToken;
            }
        }

        public override void Dispose()
        {
            if (!this.isDisposed)
            {
                this.authKeyHashFunction.Dispose();
                this.isDisposed = true;
            }
        }

        private static string NormalizeAuthorizationPayload(string input)
        {
            const int expansionBuffer = 12;
            StringBuilder builder = new StringBuilder(input.Length + expansionBuffer);
            for (int i = 0; i < input.Length; i++)
            {
                switch (input[i])
                {
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '/':
                        builder.Append("\\/");
                        break;
                    default:
                        builder.Append(input[i]);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
