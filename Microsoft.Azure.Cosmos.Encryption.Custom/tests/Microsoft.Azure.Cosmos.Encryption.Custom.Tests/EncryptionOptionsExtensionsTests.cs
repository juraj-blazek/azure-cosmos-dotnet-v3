namespace Microsoft.Azure.Cosmos.Encryption.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Azure.Cosmos.Encryption.Custom;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class EncryptionOptionsExtensionsTests
    {
        [TestMethod]
        public void Validate_EncryptionOptions_Throws_WhenNullDataEncryptionKeyId()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = null,
                EncryptionAlgorithm = "something",
                PathsToEncrypt = new List<string>()
            }.Validate());
        }

        [TestMethod]
        public void Validate_EncryptionOptions_Throws_WhenNullEncryptionAlgorithm()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = "something",
                EncryptionAlgorithm = null,
                PathsToEncrypt = new List<string>()
            }.Validate());
        }

        [TestMethod]
        public void Validate_EncryptionOptions_Throws_WhenNullPathsToEncrypt()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = "something",
                EncryptionAlgorithm = "something",
                PathsToEncrypt = null
            }.Validate());
        }

        [TestMethod]
        public void Validate_EncryptionOptions_Throws_WhenNegativeMinimalCompressedLength()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = "something",
                EncryptionAlgorithm = "something",
                PathsToEncrypt = new List<string>(),
                CompressionOptions = new CompressionOptions()
                {
                    MinimalCompressedLength = -1
                }
            }.Validate());
        }

        [TestMethod]
        public void Validate_EncryptionOptions_Throws_WhenDuplicatePaths()
        {
            InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = "something",
                EncryptionAlgorithm = "something",
                PathsToEncrypt = new List<string> { "/path", "/path" }
            }.Validate());

            Assert.AreEqual("Duplicate paths in PathsToEncrypt.", exception.Message);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("path")]
        [DataRow("/id")]
        [DataRow("/path/")]
        [DataRow("/path/sub-path")]
        public void Validate_EncryptionOptions_Throws_WhenInvalidPath(string path)
        {
            InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = "something",
                EncryptionAlgorithm = "something",
                PathsToEncrypt = new List<string> { path }
            }.Validate());

            Assert.AreEqual($"Invalid path '{path}' in PathsToEncrypt.", exception.Message);
        }

#if ENCRYPTION_CUSTOM_PREVIEW && NET8_0_OR_GREATER
        [TestMethod]
        public void Validate_EncryptionOptions_DoesNotThrow_WhenAllOk()
        {
            try
            {
                new EncryptionOptions()
                {
                    DataEncryptionKeyId = "something",
                    EncryptionAlgorithm = "MdeAeadAes256CbcHmac256Randomized",
                    PathsToEncrypt = new List<string>(),
                    JsonProcessor = JsonProcessor.Stream
                }.Validate();
            }
            catch (Exception)
            {
                Assert.Fail("Should not throw");
            }
        }

        [TestMethod]
        public void Validate_EncryptionOptions_Throws_WhenStreamingAndNotMde()
        {
            InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() => new EncryptionOptions()
            {
                DataEncryptionKeyId = "something",
                EncryptionAlgorithm = "something",
                PathsToEncrypt = new List<string>(),
                JsonProcessor = JsonProcessor.Stream,
            }.Validate());

            Assert.AreEqual("JsonProcessor.Stream can be used only with MdeAeadAes256CbcHmac256Randomized encryption algorithm.", exception.Message);
        }
#endif

        [TestMethod]
        public void Validate_CompressionOptions_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new CompressionOptions()
            {
                MinimalCompressedLength = -1
            }.Validate());
        }
    }
}
