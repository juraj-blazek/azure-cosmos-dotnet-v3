//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    /// <summary>
    /// Options for compression of encrypted data.
    /// </summary>
    internal sealed class CompressionOptions
    {
        /// <summary>
        /// Gets or sets the compression algorithm to use.
        /// </summary>
        internal CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.Deflate;

        /// <summary>
        /// Gets or sets the minimum property size to apply compression.
        /// </summary>
        internal int PropertySizeThreshold { get; set; } = 0;
    }
}