//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System.IO.Compression;

    /// <summary>
    /// Options for compression of encrypted data.
    /// </summary>
    internal sealed class CompressionOptions
    {
        /// <summary>
        /// Gets or sets the compression level to be used.
        /// </summary>
        internal CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;
    }
}