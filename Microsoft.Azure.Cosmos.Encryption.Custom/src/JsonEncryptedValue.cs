// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    internal class JsonEncryptedValue
    {
        internal byte[] Value { get; private set; }

        internal int Offset { get; private set; }

        internal int Length { get; private set; }

        public JsonEncryptedValue(byte[] value, int offset, int length)
        {
            this.Value = value;
            this.Offset = offset;
            this.Length = length;
        }
    }
}
