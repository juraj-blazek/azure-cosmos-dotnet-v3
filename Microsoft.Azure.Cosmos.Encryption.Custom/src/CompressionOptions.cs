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
        }

        public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.None;

        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        public int MinimalCompressedLength { get; set; } = 512;
    }
}
