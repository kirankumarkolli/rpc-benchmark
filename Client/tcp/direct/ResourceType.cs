//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Documents
{
    using System;
    using System.Collections.Generic;

    internal enum ResourceType : int
    {
        // Keep in sync with ResourceType enum in backend native.
        Unknown = -1,
        Database = 0,
        Collection = 1,
        Document = 2,
        Attachment = 3,
        User = 4,
        Permission = 5,
        Conflict = 107,
        Record = 108,
        StoredProcedure = 109,
        Trigger = 110,
        UserDefinedFunction = 111,
        BatchApply = 112,
        Offer = 113,
        DatabaseAccount = 118,
        SchemaContainer = 123,
        Schema = 124,
        PartitionKeyRange = 125,
        ComputeGatewayCharges = 131,
        UserDefinedType = 133,
        Batch = 135,
        PartitionKey = 136,
        Snapshot = 137,
        PartitionedSystemDocument = 138,

        ClientEncryptionKey = 141,
        Transaction = 145,

        RoleDefinition = 146,
        RoleAssignment = 147,

        // These names make it unclear what they map to in ResourceType.
        Key = -2,
        Media = -3,
        Address = -5,
        ControllerService = -6,
    }

    internal static class ResourceTypeExtensions
    {
        private static Dictionary<int, string> resourceTypeNames = new Dictionary<int, string>();

        static ResourceTypeExtensions()
        {
            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                ResourceTypeExtensions.resourceTypeNames[(int)type] = type.ToString();
            }
        }

        public static string ToResourceTypeString(this ResourceType type)
        {
            return ResourceTypeExtensions.resourceTypeNames[(int)type];
        }

        /// <summary>
        /// Resources for which this method returns true, are spread between multiple
        /// partitions.
        /// </summary>
        public static bool IsPartitioned(this ResourceType type)
        {
            return type == ResourceType.Document ||
                type == ResourceType.Attachment ||
                type == ResourceType.Conflict ||
                type == ResourceType.PartitionKey ||
                type == ResourceType.PartitionedSystemDocument;
        }

        public static bool IsCollectionChild(this ResourceType type)
        {
            return type == ResourceType.Document ||
                   type == ResourceType.Attachment ||
                   type == ResourceType.Conflict ||
                   type == ResourceType.Schema ||
                   type == ResourceType.PartitionKey ||
                   type == ResourceType.PartitionedSystemDocument ||
                   type.IsScript();
        }

        public static bool IsScript(this ResourceType type)
        {
            return type == ResourceType.UserDefinedFunction || type == ResourceType.Trigger || type == ResourceType.StoredProcedure;
        }
    }
}
