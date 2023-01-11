namespace Microsoft.Azure.Cosmos.Encryption.Custom.Benchmarks
{
    using System.IO.Compression;
    using BenchmarkDotNet.Attributes;
    using Microsoft.Data.Encryption.Cryptography;
    using Moq;

    [RPlotExporter]
    public partial class EncryptionBenchmark
    {
        private static readonly byte[] DekData = Enumerable.Repeat((byte)0, 32).ToArray();
        private TestDoc? testDoc;
        private CosmosEncryptor? encryptor;

        [Params(0, 1, 10, 100, 1000)]
        public int DocumentSizeInKb { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            DataEncryptionKeyProperties dekProperties = new (
                "id",
                CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized,
                DekData,
                new EncryptionKeyWrapMetadata("name", "value"), DateTime.UtcNow);

            Mock<EncryptionKeyStoreProvider> storeProvider = new ();
            storeProvider
                .Setup(x => x.UnwrapKey(It.IsAny<string>(), It.IsAny<KeyEncryptionKeyAlgorithm>(), It.IsAny<byte[]>()))
                .Returns(DekData);

            MdeEncryptionAlgorithm mdeDek = new(dekProperties, EncryptionType.Deterministic, storeProvider.Object, cacheTimeToLive: null);
            
            Mock<DataEncryptionKeyProvider> keyProvider = new();
            keyProvider
                .Setup(x => x.FetchDataEncryptionKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mdeDek);

            this.encryptor = new(keyProvider.Object);

            this.testDoc = TestDoc.Create(approximateSize: this.DocumentSizeInKb * 1024);
        }

        [Benchmark]
        public async Task EncryptDecrypt_NoCompression()
        {
            EncryptionOptions encryptionOptions = CreateEncryptionOptions();            
            await this.EncryptDecrypt(encryptionOptions);
        }

        [Benchmark]
        public async Task EncryptDecrypt_CompressionFastest()
        {
            EncryptionOptions encryptionOptions = CreateEncryptionOptions(CompressionLevel.Fastest);
            await this.EncryptDecrypt(encryptionOptions);
        }

        [Benchmark]
        public async Task EncryptDecrypt_CompressionOptimal()
        {
            EncryptionOptions encryptionOptions = CreateEncryptionOptions(CompressionLevel.Optimal);
            await this.EncryptDecrypt(encryptionOptions);
        }

        [Benchmark]
        public async Task EncryptDecrypt_CompressionSmallestSize()
        {
            EncryptionOptions encryptionOptions = CreateEncryptionOptions(CompressionLevel.SmallestSize);
            await this.EncryptDecrypt(encryptionOptions);
        }

        private async Task EncryptDecrypt(EncryptionOptions encryptionOptions)
        {
            using Stream encryptedStream = await EncryptionProcessor.EncryptAsync(
                 EncryptionProcessor.BaseSerializer.ToStream(this.testDoc),
                 this.encryptor,
                 encryptionOptions,
                 new CosmosDiagnosticsContext(),
                 CancellationToken.None);

            long encryptedLength = encryptedStream.Length;

            (Stream decryptedStream, DecryptionContext decryptionContext) = await EncryptionProcessor.DecryptAsync(
                encryptedStream,
                this.encryptor,
                new CosmosDiagnosticsContext(),
                CancellationToken.None);

            //long decryptedLength = decryptedStream.Length;
            //string compression = encryptionOptions.CompressionOptions?.CompressionLevel.ToString() ?? "None";
            //Console.WriteLine($"Compression: {compression}, plaintext length: {decryptedLength}, encrypted length: {encryptedLength}, final size ratio: {encryptedLength * 100 / decryptedLength}%");

            decryptedStream.Dispose();
        }

        private static EncryptionOptions CreateEncryptionOptions(CompressionLevel? level = null)
        {
            EncryptionOptions options = new()
            {
                DataEncryptionKeyId = "dekId",
                EncryptionAlgorithm = CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized,
                PathsToEncrypt = TestDoc.PathsToEncrypt
            };

            if (level.HasValue)
            {
                options.CompressionOptions = new CompressionOptions
                {
                    CompressionLevel = level.Value,
                };
            }

            return options;
        }
    }
}