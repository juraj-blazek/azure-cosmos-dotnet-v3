// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System.Text.Json.Serialization;

    internal class EncryptionPropertiesWrap
    {
        [JsonPropertyName(Constants.EncryptedInfo)]
        public EncryptionProperties EncryptionProperties { get; }

        public EncryptionPropertiesWrap(EncryptionProperties encryptionProperties)
        {
            this.EncryptionProperties = encryptionProperties;
        }
    }
}
