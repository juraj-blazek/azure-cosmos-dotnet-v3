//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Encryption.Cryptography.Serializers;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Allows encrypting items in a container using Cosmos Legacy Encryption Algorithm and MDE Encryption Algorithm.
    /// </summary>
    internal static class EncryptionProcessor
    {
        private const int Int32Size = sizeof(int);
        private const int TypeMarkerIndex = 0;
        private const int DataIndex = TypeMarkerIndex + 1;

        private const int CompressedAlgorithmIndex = TypeMarkerIndex + 1;
        private const int CompressedOriginalDataSizeIndex = CompressedAlgorithmIndex + 1;
        private const int CompressedTypeMarkerIndex = CompressedOriginalDataSizeIndex + Int32Size;
        private const int CompressedDataIndex = CompressedTypeMarkerIndex + 1;

        private static readonly SqlSerializerFactory SqlSerializerFactory = new SqlSerializerFactory();

        // UTF-8 encoding.
        private static readonly SqlVarCharSerializer SqlVarCharSerializer = new SqlVarCharSerializer(size: -1, codePageCharacterEncoding: 65001);

        private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings()
        {
            DateParseHandling = DateParseHandling.None,
        };

        internal static readonly CosmosJsonDotNetSerializer BaseSerializer = new CosmosJsonDotNetSerializer(JsonSerializerSettings);

        /// <remarks>
        /// If there isn't any PathsToEncrypt, input stream will be returned without any modification.
        /// Else input stream will be disposed, and a new stream is returned.
        /// In case of an exception, input stream won't be disposed, but position will be end of stream.
        /// </remarks>
        public static async Task<Stream> EncryptAsync(
            Stream input,
            Encryptor encryptor,
            EncryptionOptions encryptionOptions,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            EncryptionProcessor.ValidateInputForEncrypt(
                input,
                encryptor,
                encryptionOptions);

            if (!encryptionOptions.PathsToEncrypt.Any())
            {
                return input;
            }

            if (!encryptionOptions.PathsToEncrypt.Distinct().SequenceEqual(encryptionOptions.PathsToEncrypt))
            {
                throw new InvalidOperationException("Duplicate paths in PathsToEncrypt passed via EncryptionOptions.");
            }

            foreach (string path in encryptionOptions.PathsToEncrypt)
            {
                if (string.IsNullOrWhiteSpace(path) || path[0] != '/' || path.LastIndexOf('/') != 0)
                {
                    throw new InvalidOperationException($"Invalid path {path ?? string.Empty}, {nameof(encryptionOptions.PathsToEncrypt)}");
                }

                if (string.Equals(path.Substring(1), "id"))
                {
                    throw new InvalidOperationException($"{nameof(encryptionOptions.PathsToEncrypt)} includes a invalid path: '{path}'.");
                }
            }

            JObject itemJObj = EncryptionProcessor.BaseSerializer.FromStream<JObject>(input);
            List<string> pathsEncrypted = new List<string>();
            EncryptionProperties encryptionProperties = null;
            byte[] plainText = null;
            byte[] cipherText = null;
            SerializationResult serialized;

            switch (encryptionOptions.EncryptionAlgorithm)
            {
                case CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized:

                    foreach (string pathToEncrypt in encryptionOptions.PathsToEncrypt)
                    {
                        string propertyName = pathToEncrypt.Substring(1);
                        if (!itemJObj.TryGetValue(propertyName, out JToken propertyValue))
                        {
                            continue;
                        }

                        if (propertyValue.Type == JTokenType.Null)
                        {
                            continue;
                        }

                        serialized = EncryptionProcessor.Serialize(propertyValue, encryptionOptions.CompressionOptions);

                        cipherText = await encryptor.EncryptAsync(
                            serialized.Data,
                            encryptionOptions.DataEncryptionKeyId,
                            encryptionOptions.EncryptionAlgorithm);

                        if (cipherText == null)
                        {
                            throw new InvalidOperationException($"{nameof(Encryptor)} returned null cipherText from {nameof(EncryptAsync)}.");
                        }

                        int offset = DataIndex;
                        if (serialized.CompressionAlgorithm != CompressionAlgorithm.None)
                        {
                            offset = CompressedDataIndex;
                        }

                        byte[] cipherTextWithTypeMarker = new byte[cipherText.Length + offset];

                        if (serialized.CompressionAlgorithm != CompressionAlgorithm.None)
                        {
                            cipherTextWithTypeMarker[TypeMarkerIndex] = (byte)TypeMarker.Compressed;
                            cipherTextWithTypeMarker[CompressedAlgorithmIndex] = (byte)serialized.CompressionAlgorithm;

                            Memory<byte> dataSize = new(cipherTextWithTypeMarker, CompressedOriginalDataSizeIndex, Int32Size);
                            BinaryPrimitives.WriteInt32BigEndian(dataSize.Span, serialized.UncompressedDataLength);

                            cipherTextWithTypeMarker[CompressedTypeMarkerIndex] = (byte)serialized.TypeMarker;
                        }
                        else
                        {
                            cipherTextWithTypeMarker[TypeMarkerIndex] = (byte)serialized.TypeMarker;
                        }

                        Buffer.BlockCopy(cipherText, 0, cipherTextWithTypeMarker, offset, cipherText.Length);
                        itemJObj[propertyName] = cipherTextWithTypeMarker;
                        pathsEncrypted.Add(pathToEncrypt);
                    }

                    encryptionProperties = new EncryptionProperties(
                          encryptionFormatVersion: 3,
                          encryptionOptions.EncryptionAlgorithm,
                          encryptionOptions.DataEncryptionKeyId,
                          encryptedData: null,
                          pathsEncrypted);
                    break;

                case CosmosEncryptionAlgorithm.AEAes256CbcHmacSha256Randomized:

                    JObject toEncryptJObj = new JObject();

                    foreach (string pathToEncrypt in encryptionOptions.PathsToEncrypt)
                    {
                        string propertyName = pathToEncrypt.Substring(1);
                        if (!itemJObj.TryGetValue(propertyName, out JToken propertyValue))
                        {
                            continue;
                        }

                        toEncryptJObj.Add(propertyName, propertyValue.Value<JToken>());
                        itemJObj.Remove(propertyName);
                    }

                    MemoryStream memoryStream = EncryptionProcessor.BaseSerializer.ToStream<JObject>(toEncryptJObj);
                    Debug.Assert(memoryStream != null);
                    Debug.Assert(memoryStream.TryGetBuffer(out _));
                    plainText = memoryStream.ToArray();

                    cipherText = await encryptor.EncryptAsync(
                        plainText,
                        encryptionOptions.DataEncryptionKeyId,
                        encryptionOptions.EncryptionAlgorithm,
                        cancellationToken);

                    if (cipherText == null)
                    {
                        throw new InvalidOperationException($"{nameof(Encryptor)} returned null cipherText from {nameof(EncryptAsync)}.");
                    }

                    encryptionProperties = new EncryptionProperties(
                          encryptionFormatVersion: 2,
                          encryptionOptions.EncryptionAlgorithm,
                          encryptionOptions.DataEncryptionKeyId,
                          encryptedData: cipherText,
                          encryptionOptions.PathsToEncrypt);
                    break;

                default:
                    throw new NotSupportedException($"Encryption Algorithm : {encryptionOptions.EncryptionAlgorithm} is not supported.");
            }

            itemJObj.Add(Constants.EncryptedInfo, JObject.FromObject(encryptionProperties));
            input.Dispose();
            return EncryptionProcessor.BaseSerializer.ToStream(itemJObj);
        }

        /// <remarks>
        /// If there isn't any data that needs to be decrypted, input stream will be returned without any modification.
        /// Else input stream will be disposed, and a new stream is returned.
        /// In case of an exception, input stream won't be disposed, but position will be end of stream.
        /// </remarks>
        public static async Task<(Stream, DecryptionContext)> DecryptAsync(
            Stream input,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            if (input == null)
            {
                return (input, null);
            }

            Debug.Assert(input.CanSeek);
            Debug.Assert(encryptor != null);
            Debug.Assert(diagnosticsContext != null);

            JObject itemJObj = EncryptionProcessor.RetrieveItem(input);
            JObject encryptionPropertiesJObj = EncryptionProcessor.RetrieveEncryptionProperties(itemJObj);

            if (encryptionPropertiesJObj == null)
            {
                input.Position = 0;
                return (input, null);
            }

            EncryptionProperties encryptionProperties = encryptionPropertiesJObj.ToObject<EncryptionProperties>();
            DecryptionContext decryptionContext = encryptionProperties.EncryptionAlgorithm switch
            {
                CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized => await EncryptionProcessor.MdeEncAlgoDecryptObjectAsync(
                    itemJObj,
                    encryptor,
                    encryptionProperties,
                    diagnosticsContext,
                    cancellationToken),
                CosmosEncryptionAlgorithm.AEAes256CbcHmacSha256Randomized => await EncryptionProcessor.LegacyEncAlgoDecryptContentAsync(
                    itemJObj,
                    encryptionProperties,
                    encryptor,
                    diagnosticsContext,
                    cancellationToken),
                _ => throw new NotSupportedException($"Encryption Algorithm : {encryptionProperties.EncryptionAlgorithm} is not supported."),
            };

            input.Dispose();
            return (EncryptionProcessor.BaseSerializer.ToStream(itemJObj), decryptionContext);
        }

        public static async Task<(JObject, DecryptionContext)> DecryptAsync(
            JObject document,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            Debug.Assert(document != null);

            Debug.Assert(encryptor != null);

            JObject encryptionPropertiesJObj = EncryptionProcessor.RetrieveEncryptionProperties(document);

            if (encryptionPropertiesJObj == null)
            {
                return (document, null);
            }

            EncryptionProperties encryptionProperties = encryptionPropertiesJObj.ToObject<EncryptionProperties>();
            DecryptionContext decryptionContext = encryptionProperties.EncryptionAlgorithm switch
            {
                CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized => await EncryptionProcessor.MdeEncAlgoDecryptObjectAsync(
                    document,
                    encryptor,
                    encryptionProperties,
                    diagnosticsContext,
                    cancellationToken),
                CosmosEncryptionAlgorithm.AEAes256CbcHmacSha256Randomized => await EncryptionProcessor.LegacyEncAlgoDecryptContentAsync(
                    document,
                    encryptionProperties,
                    encryptor,
                    diagnosticsContext,
                    cancellationToken),
                _ => throw new NotSupportedException($"Encryption Algorithm : {encryptionProperties.EncryptionAlgorithm} is not supported."),
            };

            return (document, decryptionContext);
        }

        private static async Task<DecryptionContext> MdeEncAlgoDecryptObjectAsync(
            JObject document,
            Encryptor encryptor,
            EncryptionProperties encryptionProperties,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            JObject plainTextJObj = new JObject();
            foreach (string path in encryptionProperties.EncryptedPaths)
            {
                string propertyName = path.Substring(1);
                if (!document.TryGetValue(propertyName, out JToken propertyValue))
                {
                    continue;
                }

                byte[] cipherTextWithTypeMarker = propertyValue.ToObject<byte[]>();

                if (cipherTextWithTypeMarker == null)
                {
                    continue;
                }

                int dataIndex = 1;
                CompressionAlgorithm compressionAlgorithm = CompressionAlgorithm.None;
                TypeMarker typeMarker = (TypeMarker)cipherTextWithTypeMarker[TypeMarkerIndex];
                int originalDataLength = 0;

                if (typeMarker == TypeMarker.Compressed)
                {
                    dataIndex = CompressedDataIndex;
                    compressionAlgorithm = (CompressionAlgorithm)cipherTextWithTypeMarker[CompressedAlgorithmIndex];

                    Memory<byte> dataSize = new(cipherTextWithTypeMarker, CompressedOriginalDataSizeIndex, Int32Size);
                    originalDataLength = BinaryPrimitives.ReadInt32BigEndian(dataSize.Span);

                    typeMarker = (TypeMarker)cipherTextWithTypeMarker[CompressedTypeMarkerIndex];
                }

                int dataLength = cipherTextWithTypeMarker.Length - dataIndex;

                byte[] cipherText = new byte[dataLength];
                Buffer.BlockCopy(cipherTextWithTypeMarker, dataIndex, cipherText, 0, dataLength);

                byte[] plainText = await EncryptionProcessor.MdeEncAlgoDecryptPropertyAsync(
                    encryptionProperties,
                    cipherText,
                    encryptor,
                    diagnosticsContext,
                    cancellationToken);

                plainText = TryDecompress(plainText, compressionAlgorithm, originalDataLength);

                EncryptionProcessor.DeserializeAndAddProperty(
                    typeMarker,
                    plainText,
                    plainTextJObj,
                    propertyName);
            }

            List<string> pathsDecrypted = new List<string>();
            foreach (JProperty property in plainTextJObj.Properties())
            {
                document[property.Name] = property.Value;
                pathsDecrypted.Add("/" + property.Name);
            }

            DecryptionContext decryptionContext = EncryptionProcessor.CreateDecryptionContext(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            document.Remove(Constants.EncryptedInfo);
            return decryptionContext;
        }

        private static DecryptionContext CreateDecryptionContext(
            List<string> pathsDecrypted,
            string dataEncryptionKeyId)
        {
            DecryptionInfo decryptionInfo = new DecryptionInfo(
                pathsDecrypted,
                dataEncryptionKeyId);

            DecryptionContext decryptionContext = new DecryptionContext(
                new List<DecryptionInfo>() { decryptionInfo });

            return decryptionContext;
        }

        private static async Task<byte[]> MdeEncAlgoDecryptPropertyAsync(
            EncryptionProperties encryptionProperties,
            byte[] cipherText,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            if (encryptionProperties.EncryptionFormatVersion != 3)
            {
                throw new NotSupportedException($"Unknown encryption format version: {encryptionProperties.EncryptionFormatVersion}. Please upgrade your SDK to the latest version.");
            }

            byte[] plainText = await encryptor.DecryptAsync(
                cipherText,
                encryptionProperties.DataEncryptionKeyId,
                encryptionProperties.EncryptionAlgorithm,
                cancellationToken)
                ?? throw new InvalidOperationException($"{nameof(Encryptor)} returned null plainText from {nameof(DecryptAsync)}.");

            return plainText;
        }

        private static async Task<DecryptionContext> LegacyEncAlgoDecryptContentAsync(
            JObject document,
            EncryptionProperties encryptionProperties,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            if (encryptionProperties.EncryptionFormatVersion != 2)
            {
                throw new NotSupportedException($"Unknown encryption format version: {encryptionProperties.EncryptionFormatVersion}. Please upgrade your SDK to the latest version.");
            }

            byte[] plainText = await encryptor.DecryptAsync(
                encryptionProperties.EncryptedData,
                encryptionProperties.DataEncryptionKeyId,
                encryptionProperties.EncryptionAlgorithm,
                cancellationToken)
                ?? throw new InvalidOperationException($"{nameof(Encryptor)} returned null plainText from {nameof(DecryptAsync)}.");

            JObject plainTextJObj;
            using (MemoryStream memoryStream = new MemoryStream(plainText))
            using (StreamReader streamReader = new StreamReader(memoryStream))
            using (JsonTextReader jsonTextReader = new JsonTextReader(streamReader))
            {
                plainTextJObj = JObject.Load(jsonTextReader);
            }

            List<string> pathsDecrypted = new List<string>();
            foreach (JProperty property in plainTextJObj.Properties())
            {
                document.Add(property.Name, property.Value);
                pathsDecrypted.Add("/" + property.Name);
            }

            DecryptionContext decryptionContext = EncryptionProcessor.CreateDecryptionContext(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            document.Remove(Constants.EncryptedInfo);

            return decryptionContext;
        }

        private static void ValidateInputForEncrypt(
            Stream input,
            Encryptor encryptor,
            EncryptionOptions encryptionOptions)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (encryptor == null)
            {
                throw new ArgumentNullException(nameof(encryptor));
            }

            if (encryptionOptions == null)
            {
                throw new ArgumentNullException(nameof(encryptionOptions));
            }

            if (string.IsNullOrWhiteSpace(encryptionOptions.DataEncryptionKeyId))
            {
                throw new ArgumentNullException(nameof(encryptionOptions.DataEncryptionKeyId));
            }

            if (string.IsNullOrWhiteSpace(encryptionOptions.EncryptionAlgorithm))
            {
                throw new ArgumentNullException(nameof(encryptionOptions.EncryptionAlgorithm));
            }

            if (encryptionOptions.PathsToEncrypt == null)
            {
                throw new ArgumentNullException(nameof(encryptionOptions.PathsToEncrypt));
            }
        }

        private static JObject RetrieveItem(
            Stream input)
        {
            Debug.Assert(input != null);

            JObject itemJObj;
            using (StreamReader sr = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            using (JsonTextReader jsonTextReader = new JsonTextReader(sr))
            {
                JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64, // https://github.com/advisories/GHSA-5crp-9r3c-p9vr
                };

                itemJObj = JsonSerializer.Create(jsonSerializerSettings).Deserialize<JObject>(jsonTextReader);
            }

            return itemJObj;
        }

        private static JObject RetrieveEncryptionProperties(
            JObject item)
        {
            JProperty encryptionPropertiesJProp = item.Property(Constants.EncryptedInfo);
            JObject encryptionPropertiesJObj = null;
            if (encryptionPropertiesJProp?.Value != null && encryptionPropertiesJProp.Value.Type == JTokenType.Object)
            {
                encryptionPropertiesJObj = (JObject)encryptionPropertiesJProp.Value;
            }

            return encryptionPropertiesJObj;
        }

        private static SerializationResult Serialize(JToken propertyValue, CompressionOptions compressionOptions)
        {
            switch (propertyValue.Type)
            {
                case JTokenType.Undefined:
                    Debug.Assert(false, "Undefined value cannot be in the JSON");
                    return SerializationResult.Undefined;
                case JTokenType.Null:
                    Debug.Assert(false, "Null type should have been handled by caller");
                    return SerializationResult.Null;
                case JTokenType.Boolean:
                    return SerializationResult.Uncompressed(TypeMarker.Boolean, SqlSerializerFactory.GetDefaultSerializer<bool>().Serialize(propertyValue.ToObject<bool>()));
                case JTokenType.Float:
                    return SerializationResult.Uncompressed(TypeMarker.Double, SqlSerializerFactory.GetDefaultSerializer<double>().Serialize(propertyValue.ToObject<double>()));
                case JTokenType.Integer:
                    return SerializationResult.Uncompressed(TypeMarker.Long, SqlSerializerFactory.GetDefaultSerializer<long>().Serialize(propertyValue.ToObject<long>()));
                case JTokenType.String:
                    return SerializeVarChar(TypeMarker.String, propertyValue.ToObject<string>(), compressionOptions);
                case JTokenType.Array:
                    return SerializeVarChar(TypeMarker.Array, propertyValue.ToString(), compressionOptions);
                case JTokenType.Object:
                    return SerializeVarChar(TypeMarker.Object, propertyValue.ToString(), compressionOptions);
                default:
                    throw new InvalidOperationException($" Invalid or Unsupported Data Type Passed : {propertyValue.Type}");
            }
        }

        private static SerializationResult SerializeVarChar(TypeMarker typeMarker, string propertyValue, CompressionOptions compressionOptions)
        {
            byte[] serialized = SqlVarCharSerializer.Serialize(propertyValue);
            (CompressionAlgorithm compressionAlgorithm, byte[] data) = TryCompress(serialized, compressionOptions);
            return SerializationResult.Compressed(compressionAlgorithm, typeMarker, data, serialized.Length);
        }

        private static (CompressionAlgorithm compressionAlgorithm, byte[]) TryCompress(byte[] input, CompressionOptions compressionOptions)
        {
            if (compressionOptions == null || input.Length < compressionOptions.PropertySizeThreshold)
            {
                return (CompressionAlgorithm.None, input);
            }

            return compressionOptions.Algorithm switch
            {
                CompressionAlgorithm.None => (CompressionAlgorithm.None, input),
                CompressionAlgorithm.Deflate => (CompressionAlgorithm.Deflate, CompressDeflate(input)),
                _ => throw new NotSupportedException(),
            };
        }

        private static byte[] TryDecompress(byte[] input, CompressionAlgorithm compressionAlgorithm, int originalDataSize)
        {
            return compressionAlgorithm switch
            {
                CompressionAlgorithm.None => input,
                CompressionAlgorithm.Deflate => DecompressDeflate(input, originalDataSize),
                _ => throw new NotSupportedException(),
            };
        }

        private static byte[] CompressDeflate(byte[] input)
        {
            using MemoryStream ms = new (input.Length);
            using DeflateStream gz = new (ms, CompressionLevel.Fastest);
            gz.Write(input, 0, input.Length);
            gz.Flush();
            return ms.ToArray();
        }

        private static byte[] DecompressDeflate(byte[] input, int originalDataSize)
        {
            using MemoryStream msIn = new (input);
            using MemoryStream msOut = new (originalDataSize);
            using DeflateStream gz = new (msIn, CompressionMode.Decompress);
            gz.CopyTo(msOut);
            return msOut.GetBuffer();
        }

        private static void DeserializeAndAddProperty(
            TypeMarker typeMarker,
            byte[] serializedBytes,
            JObject jObject,
            string key)
        {
            switch (typeMarker)
            {
                case TypeMarker.Boolean:
                    jObject.Add(key, SqlSerializerFactory.GetDefaultSerializer<bool>().Deserialize(serializedBytes));
                    break;
                case TypeMarker.Double:
                    jObject.Add(key, SqlSerializerFactory.GetDefaultSerializer<double>().Deserialize(serializedBytes));
                    break;
                case TypeMarker.Long:
                    jObject.Add(key, SqlSerializerFactory.GetDefaultSerializer<long>().Deserialize(serializedBytes));
                    break;
                case TypeMarker.String:
                    jObject.Add(key, SqlVarCharSerializer.Deserialize(serializedBytes));
                    break;
                case TypeMarker.Array:
                    jObject.Add(key, JsonConvert.DeserializeObject<JArray>(SqlVarCharSerializer.Deserialize(serializedBytes), JsonSerializerSettings));
                    break;
                case TypeMarker.Object:
                    jObject.Add(key, JsonConvert.DeserializeObject<JObject>(SqlVarCharSerializer.Deserialize(serializedBytes), JsonSerializerSettings));
                    break;
                default:
                    Debug.Fail(string.Format("Unexpected type marker {0}", typeMarker));
                    break;
            }
        }

        private enum TypeMarker : byte
        {
            Null = 1, // not used
            String = 2,
            Double = 3,
            Long = 4,
            Boolean = 5,
            Array = 6,
            Object = 7,
            Compressed = 99,
        }

        internal static async Task<Stream> DeserializeAndDecryptResponseAsync(
            Stream content,
            Encryptor encryptor,
            CancellationToken cancellationToken)
        {
            JObject contentJObj = EncryptionProcessor.BaseSerializer.FromStream<JObject>(content);

            if (!(contentJObj.SelectToken(Constants.DocumentsResourcePropertyName) is JArray documents))
            {
                throw new InvalidOperationException("Feed Response body contract was violated. Feed response did not have an array of Documents");
            }

            foreach (JToken value in documents)
            {
                if (!(value is JObject document))
                {
                    continue;
                }

                CosmosDiagnosticsContext diagnosticsContext = CosmosDiagnosticsContext.Create(null);
                using (diagnosticsContext.CreateScope("EncryptionProcessor.DeserializeAndDecryptResponseAsync"))
                {
                    await EncryptionProcessor.DecryptAsync(
                        document,
                        encryptor,
                        diagnosticsContext,
                        cancellationToken);
                }
            }

            // the contents of contentJObj get decrypted in place for MDE algorithm model, and for legacy model _ei property is removed
            // and corresponding decrypted properties are added back in the documents.
            return EncryptionProcessor.BaseSerializer.ToStream(contentJObj);
        }

        private class SerializationResult
        {
            public CompressionAlgorithm CompressionAlgorithm { get; }

            public TypeMarker TypeMarker { get; }

            public byte[] Data { get; }

            public int UncompressedDataLength { get; }

            private SerializationResult(CompressionAlgorithm compressionAlgorithm, TypeMarker typeMarker, byte[] data, int uncompressedDataLength)
            {
                this.CompressionAlgorithm = compressionAlgorithm;
                this.TypeMarker = typeMarker;
                this.Data = data;
                this.UncompressedDataLength = uncompressedDataLength;
            }

            public static SerializationResult Undefined => Uncompressed(default);

            public static SerializationResult Null => Uncompressed(TypeMarker.Null);

            public static SerializationResult Uncompressed(TypeMarker typeMarker, byte[] data = default)
            {
                return new SerializationResult(CompressionAlgorithm.None, typeMarker, data, 0);
            }

            public static SerializationResult Compressed(CompressionAlgorithm compressionAlgorithm, TypeMarker typeMarker, byte[] data, int uncompressedDataLength)
            {
                return new SerializationResult(compressionAlgorithm, typeMarker, data, uncompressedDataLength);
            }
        }
    }
}
