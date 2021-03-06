//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Documents
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using Microsoft.Azure.Documents.Collections;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System.Collections.Specialized;
    using System.Text;

    //This is core Transport/Connection agnostic response for DocumentService.
    //It is marked internal today. If needs arises for client to do no-serialized processing
    //we can open this up to public.    
    internal sealed class DocumentServiceResponse : IDisposable
    {
        private readonly JsonSerializerSettings serializerSettings;

        private bool isDisposed = false;

        internal DocumentServiceResponse(
            Stream body, 
            INameValueCollection headers, 
            HttpStatusCode statusCode, 
            JsonSerializerSettings serializerSettings = null)
        {
            this.ResponseBody = body;
            this.Headers = headers;
            this.StatusCode = statusCode;
            this.serializerSettings = serializerSettings;
            this.SubStatusCode = this.GetSubStatusCodes();
        }

        internal DocumentServiceResponse(
            Stream body,
            INameValueCollection headers,
            HttpStatusCode statusCode,
            IClientSideRequestStatistics clientSideRequestStatistics,
            JsonSerializerSettings serializerSettings = null)
        {
            this.ResponseBody = body;
            this.Headers = headers;
            this.StatusCode = statusCode;
            this.RequestStats = clientSideRequestStatistics;
            this.serializerSettings = serializerSettings;
            this.SubStatusCode = this.GetSubStatusCodes();
        }

        public IClientSideRequestStatistics RequestStats { get; private set; }

        public string ResourceId { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public string StatusDescription { get; set; }

        internal INameValueCollection Headers { get; set; }

        // Used by V3 SDK to extend with custom formats (HybridRow, Binary etc...)
        internal static Func<Stream, JsonReader> JsonReaderFactory { get; set; }

        public NameValueCollection ResponseHeaders
        {
            get
            {
                return Headers.ToNameValueCollection();
            }
        }

        public Stream ResponseBody { get; set; }

        public SubStatusCodes SubStatusCode { get; private set; }

        public TResource GetInternalResource<TResource>(Func<TResource> constructor) where TResource : Resource
        {
            if (this.ResponseBody != null && (!this.ResponseBody.CanSeek || this.ResponseBody.Length > 0))
            {
                return Resource.LoadFromWithConstructor<TResource>(this.ResponseBody, constructor, this.serializerSettings);
            }
            else
            {
                return default(TResource);
            }
        }

        public void Dispose()
        {
            if (this.isDisposed)
            {
                return;
            }

            if (this.ResponseBody != null)
            {
                this.ResponseBody.Dispose();
                this.ResponseBody = null;
            }

            this.isDisposed = true;
        }

        internal SubStatusCodes GetSubStatusCodes()
        {
            string value = this.Headers[WFConstants.BackendHeaders.SubStatus];
            uint subStatus = 0;
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out subStatus))
            {
                return (SubStatusCodes)subStatus;
            }

            return SubStatusCodes.Unknown;
        }

        private IEnumerable<T> GetEnumerable<T>(Type resourceType, Func<JsonReader, T> callback)
        {
            // Execute the callback an each element of the page
            // For example just could get a response like this
            // {
            //    "_rid": "qHVdAImeKAQ=",
            //    "Documents": [{
            //        "id": "03230",
            //        "_rid": "qHVdAImeKAQBAAAAAAAAAA==",
            //        "_self": "dbs\/qHVdAA==\/colls\/qHVdAImeKAQ=\/docs\/qHVdAImeKAQBAAAAAAAAAA==\/",
            //        "_etag": "\"410000b0-0000-0000-0000-597916b00000\"",
            //        "_attachments": "attachments\/",
            //        "_ts": 1501107886
            //    }],
            //    "_count": 1
            // }
            // And you should execute the callback on each document in "Documents".

            if (this.ResponseBody == null)
            {
                yield break;
            }

            using (JsonReader jsonReader = Create(this.ResponseBody))
            {
                Helpers.SetupJsonReader(jsonReader, this.serializerSettings);
                string resourceName = resourceType.Name + "s";
                string propertyName = string.Empty;
                while (jsonReader.Read())
                {
                    // Keep reading until you find the property name
                    if (jsonReader.TokenType == JsonToken.PropertyName)
                    {
                        // Set the propertyName so that we can compare it to the desired resource name later.
                        propertyName = jsonReader.Value.ToString();
                    }

                    // If we found the correct property with the array of items,
                    // then execute the callback on each of them.
                    if (jsonReader.Depth == 1 &&
                        jsonReader.TokenType == JsonToken.StartArray &&
                        string.Equals(propertyName, resourceName, StringComparison.Ordinal))
                    {
                        while (jsonReader.Read() && jsonReader.Depth == 2)
                        {
                            yield return callback(jsonReader);
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a JsonReader that can read a supplied stream (assumes UTF-8 encoding).
        /// </summary>
        /// <param name="stream">the stream to read.</param>
        /// <returns>a concrete JsonReader that can read the supplied stream.</returns>
        private static Newtonsoft.Json.JsonReader Create(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (DocumentServiceResponse.JsonReaderFactory != null)
            {
                return DocumentServiceResponse.JsonReaderFactory(stream);
            }

            return new JsonTextReader(new StreamReader(stream));
        }

        private static ITypeResolver<TResource> GetTypeResolver<TResource>() where TResource : Resource, new()
        {
            ITypeResolver<TResource> typeResolver = null;

            return typeResolver;
        }

        private string GetOwnerFullName()
        {
            return this.Headers[HttpConstants.HttpHeaders.OwnerFullName];
        }
    }
}
