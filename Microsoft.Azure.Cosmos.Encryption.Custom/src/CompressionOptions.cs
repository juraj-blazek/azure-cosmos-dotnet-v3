// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System;
    using System.Collections.Generic;
    using System.IO.Compression;
    using System.Text;

    public class CompressionOptions
    {
        public enum CompressionAlgorithm
        {
            None = 0,
            GZip = 1,
            Deflate = 2,
#if NET6_0_OR_GREATER
            Brotli = 3,
#endif
        }

        public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.None;

        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        public int MinimalCompressedLength { get; set; } = 512;
    }
}
