//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
#if ENCRYPTION_CUSTOM_PREVIEW && NET8_0_OR_GREATER

namespace Microsoft.Azure.Cosmos.Encryption.Tests.Transformation
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Encryption.Custom;
    using Microsoft.Azure.Cosmos.Encryption.Custom.Transformation;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using Newtonsoft.Json.Linq;

    [TestClass]
    public class StreamProcessorTests
    {
        private Mock<Encryptor> encryptor;
        private Mock<DataEncryptionKey> dek;
        private EncryptionOptions encryptionOptions;

        [TestInitialize]
        public void Initialize()
        {
            this.encryptionOptions = new EncryptionOptions
            {
                DataEncryptionKeyId = "dek-id",
                EncryptionAlgorithm = CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized,
                JsonProcessor = JsonProcessor.Stream
            };

            this.dek = new Mock<DataEncryptionKey>(MockBehavior.Strict);
            this.dek
                .Setup(x => x.GetEncryptByteCount(It.IsAny<int>()))
                .Returns(static (int count) => count);
            this.dek
                .Setup(x => x.GetDecryptByteCount(It.IsAny<int>()))
                .Returns(static (int count) => count);
            this.dek
                .Setup(x => x.EncryptData(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<int>()))
                .Returns((byte[] plainText, int plainTextOffset, int plainTextLength, byte[] output, int outputOffset) =>
                {
                    Span<byte> outputSpan = output.AsSpan(outputOffset, plainTextLength);
                    plainText.AsSpan(plainTextOffset, plainTextLength).CopyTo(outputSpan);
                    outputSpan.Reverse();
                    return plainTextLength;
                });
            this.dek
                .Setup(x => x.DecryptData(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<int>()))
                .Returns((byte[] cipherText, int cipherTextOffset, int cipherTextLength, byte[] output, int outputOffset) =>
                {
                    Span<byte> outputSpan = output.AsSpan(outputOffset, cipherTextLength);
                    cipherText.AsSpan(cipherTextOffset, cipherTextLength).CopyTo(outputSpan);
                    outputSpan.Reverse();
                    return cipherTextLength;
                });

            this.encryptor = new Mock<Encryptor>(MockBehavior.Strict);
            this.encryptor
                .Setup(x => x.GetEncryptionKeyAsync("dek-id", CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized, default))
                .ReturnsAsync(this.dek.Object);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("string")]
        [DataRow("true")]
        [DataRow("false")]
        [DataRow("float")]
        [DataRow("double")]
        [DataRow("int")]
        [DataRow("array")]
        [DataRow("object")]
        public async Task EncryptsDecryptsData(string pathToEncrypt)
        {
            StreamProcessor sut = new();

            JObject input = this.CreateTestDoc(pathToEncrypt == null ? null : $"/{pathToEncrypt}");

            using Stream inputStream = TestCommon.ToStream(input);
            using MemoryStream outputStream = new();

            // Encryption
            await sut.EncryptStreamAsync(inputStream, outputStream, this.encryptor.Object, this.encryptionOptions, cancellationToken: default);

            // Assert
            JObject output = TestCommon.FromStream<JObject>(outputStream, leaveOpen: true);

            this.AssertEncryptedProperties(input, output, pathToEncrypt);

            // Decryption
            using MemoryStream decryptedStream = new();

            EncryptionProperties encryptionProperties = output[Constants.EncryptedInfo].ToObject<EncryptionProperties>();
            outputStream.Position = 0;
            
            await sut.DecryptStreamAsync(outputStream, decryptedStream, this.encryptor.Object, encryptionProperties, new CosmosDiagnosticsContext(), cancellationToken: default);

            JObject decrypted = TestCommon.FromStream<JObject>(decryptedStream, leaveOpen: true);
            Assert.IsTrue(JToken.DeepEquals(input, decrypted));
        }

        private JObject CreateTestDoc(params string[] pathsToEncrypt)
        {
            this.encryptionOptions.PathsToEncrypt = pathsToEncrypt;

            JObject doc = new()
            {
                { "string", "value" },
                { "null", JValue.CreateNull() },
                { "true", new JValue(true) },
                { "false", new JValue(false) },
                { "int", new JValue(42) },
                { "float", new JValue(3.14) },
                { "double", new JValue(1E100) },
                { "array", new JArray { 1, 2, 3 } },
                { "object", new JObject { { "key", "value" } } }
            };

            string json = doc.ToString(Newtonsoft.Json.Formatting.Indented);
            Assert.IsNotNull(json);

            return doc;
        }

        private void AssertEncryptedProperties(JObject input, JObject output, string encryptedPath = null)
        {
            EncryptionProperties encryptionProperties = output[Constants.EncryptedInfo].ToObject<EncryptionProperties>();
            if (encryptedPath != null)
            {
                Assert.IsTrue(encryptionProperties.EncryptedPaths.Contains($"/{encryptedPath}"));
            }
            else
            {
                Assert.AreEqual(encryptionProperties.EncryptedPaths.Count(), 0);
            }

            foreach (JProperty property in input.Properties())
            {
                JToken inputToken = input[property.Name];
                JToken outputToken = output[property.Name];

                bool isEncrypted = encryptedPath == property.Name;

                if (isEncrypted)
                {
                    Assert.AreNotEqual(inputToken.ToString(), outputToken.ToString());
                }
                else
                {
                    Assert.AreEqual(inputToken.ToString(), outputToken.ToString());
                }
            }
        }
    }
}

#endif