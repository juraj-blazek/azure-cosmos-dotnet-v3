namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Text;

    internal class CompressorFactory
    {
        public Func<Stream, Stream> CreateCompressionStream { get; }

        public Func<Stream, Stream> CreateDecompressionStream { get; }

        public CompressorFactory(CompressionOptions compressionOptions)
        {
            switch (compressionOptions.Algorithm)
            {
                case CompressionOptions.CompressionAlgorithm.GZip:
                    this.CreateCompressionStream = new Func<Stream, Stream>(input => new GZipStream(input, compressionOptions.CompressionLevel));
                    this.CreateDecompressionStream = new Func<Stream, Stream>(input => new GZipStream(input, CompressionMode.Decompress));
                    break;
                case CompressionOptions.CompressionAlgorithm.Deflate:
                    this.CreateCompressionStream = new Func<Stream, Stream>(input => new DeflateStream(input, compressionOptions.CompressionLevel));
                    this.CreateDecompressionStream = new Func<Stream, Stream>(input => new DeflateStream(input, CompressionMode.Decompress));
                    break;
            }
        }
    }
}
