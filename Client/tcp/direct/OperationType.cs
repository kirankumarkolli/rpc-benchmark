//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Documents
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;

    internal enum OperationType
    {
        // Keep in sync with RequestOperationType enum in backend native.
        Invalid = -1,
        Create = 0,
        Patch = 1,
        Read = 2,
        ReadFeed = 3,
        Delete = 4,
        Replace = 5,
        Execute = 9,
        BatchApply = 13,
        SqlQuery = 14,
        Query = 15,
        Head = 18,
        HeadFeed = 19,
        Upsert = 20,
        AddComputeGatewayRequestCharges = 37,
        Batch = 40,
        QueryPlan = 41,

        CompleteUserTransaction = 52,

        // These names make it unclear what they map to in RequestOperationType.
        ExecuteJavaScript = -2,
    }

    internal static class OperationTypeExtensions
    {
        private static readonly Dictionary<int, string> OperationTypeNames = new Dictionary<int, string>();

        static OperationTypeExtensions()
        {
            foreach (OperationType type in Enum.GetValues(typeof(OperationType)))
            {
                OperationTypeExtensions.OperationTypeNames[(int)type] = type.ToString();
            }
        }

        public static string ToOperationTypeString(this OperationType type)
        {
            return OperationTypeExtensions.OperationTypeNames[(int)type];
        }

        public static bool IsWriteOperation(this OperationType type)
        {
            return type == OperationType.Create ||
                   type == OperationType.Patch ||
                   type == OperationType.Delete ||
                   type == OperationType.Replace ||
                   type == OperationType.ExecuteJavaScript ||
                   type == OperationType.BatchApply ||
                   type == OperationType.Batch ||
                   type == OperationType.Upsert ||
                   type == OperationType.CompleteUserTransaction
                   ;
        }

        public static bool IsPointOperation(this OperationType type)
        {
            return type == OperationType.Create ||
                    type == OperationType.Delete ||
                    type == OperationType.Read ||
                    type == OperationType.Patch ||
                    type == OperationType.Upsert ||
                    type == OperationType.Replace;
        }

        public static bool IsReadOperation(this OperationType type)
        {
            return type == OperationType.Read ||
                   type == OperationType.ReadFeed ||
                   type == OperationType.Query ||
                   type == OperationType.SqlQuery ||
                   type == OperationType.Head ||
                   type == OperationType.HeadFeed ||
                   type == OperationType.QueryPlan;
        }

        /// <summary>
        /// Mapping the given operation type to the corresponding HTTP verb.
        /// </summary>
        /// <param name="operationType">The operation type.</param>
        /// <returns>The corresponding HTTP verb.</returns>
        public static string GetHttpMethod(this OperationType operationType)
        {
            switch (operationType)
            {
                case OperationType.Create:
                case OperationType.ExecuteJavaScript:
                case OperationType.Query:
                case OperationType.SqlQuery:
                case OperationType.Upsert:
                case OperationType.BatchApply:
                case OperationType.Batch:
                case OperationType.QueryPlan:
                case OperationType.CompleteUserTransaction:
                    return HttpConstants.HttpMethods.Post;

                case OperationType.Delete:
                    return HttpConstants.HttpMethods.Delete;

                case OperationType.Read:
                case OperationType.ReadFeed:
                
                    return HttpConstants.HttpMethods.Get;

                case OperationType.Replace:
                    return HttpConstants.HttpMethods.Put;

                case OperationType.Patch:
                    return HttpConstants.HttpMethods.Patch;

                case OperationType.Head:
                case OperationType.HeadFeed:
                    return HttpConstants.HttpMethods.Head;

                default:
                    string message = string.Format(CultureInfo.InvariantCulture, "Unsupported operation type: {0}.", operationType);
                    Debug.Assert(false, message);
                    throw new NotImplementedException(message);
            }
        }
    }
}
