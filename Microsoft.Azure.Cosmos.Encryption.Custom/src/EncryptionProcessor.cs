//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
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
        private static readonly SqlSerializerFactory SqlSerializerFactory = new SqlSerializerFactory();

        // UTF-8 encoding.
        private static readonly SqlVarCharSerializer SqlVarCharSerializer = new SqlVarCharSerializer(size: -1, codePageCharacterEncoding: 65001);
        private static readonly SqlBitSerializer SqlBoolSerializer = new SqlBitSerializer();
        private static readonly SqlFloatSerializer SqlDoubleSerializer = new SqlFloatSerializer();
        private static readonly SqlBigIntSerializer SqlLongSerializer = new SqlBigIntSerializer();

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
            _ = diagnosticsContext;

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

            List<string> pathsEncrypted = new List<string>();
            EncryptionProperties encryptionProperties = null;
            byte[] plainText = null;
            byte[] cipherText = null;
            TypeMarker typeMarker;

            using ArrayPoolManager arrayPoolManager = new ArrayPoolManager();

            switch (encryptionOptions.EncryptionAlgorithm)
            {
                case CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized:
                    {
                        Dictionary<string, int> pathsCompressed = new Dictionary<string, int>();
                        JsonNode document = JsonNode.Parse(input);
                        JsonObject itemJObj = document.Root.AsObject();

                        DataEncryptionKey encryptionKey = await encryptor.GetEncryptionKeyAsync(encryptionOptions.DataEncryptionKeyId, encryptionOptions.EncryptionAlgorithm);

                        CompressorFactory compressorFactory = encryptionOptions.CompressionOptions.Algorithm != CompressionOptions.CompressionAlgorithm.None ? new CompressorFactory(encryptionOptions.CompressionOptions) : null;

                        bool compressedProperties = false;

                        foreach (string pathToEncrypt in encryptionOptions.PathsToEncrypt)
                        {
                            string propertyName = pathToEncrypt.Substring(1);
                            if (!itemJObj.TryGetPropertyValue(propertyName, out JsonNode propertyValue))
                            {
                                continue;
                            }

                            if (propertyValue == null || propertyValue.GetValueKind() == JsonValueKind.Null)
                            {
                                continue;
                            }

                            (typeMarker, plainText, int plainTextLength) = EncryptionProcessor.Serialize(propertyValue, arrayPoolManager);

                            if (plainText == null)
                            {
                                continue;
                            }

                            if (compressorFactory != null && plainTextLength >= encryptionOptions.CompressionOptions.MinimalCompressedLength)
                            {
                                byte[] compressedText = arrayPoolManager.Rent(plainTextLength);
                                using MemoryStream stream = new MemoryStream(compressedText);
                                using Stream compressionStream = compressorFactory.CreateCompressionStream(stream);
                                await compressionStream.WriteAsync(plainText, 0, plainTextLength);
                                await compressionStream.FlushAsync();
                                int compressedLength = (int)stream.Position;

                                int cipherTextLength = encryptionKey.GetEncryptByteCount(compressedLength);
                                byte[] cipherTextWithTypeMarker = arrayPoolManager.Rent(compressedLength + 1);
                                cipherTextWithTypeMarker[0] = (byte)typeMarker;

                                int encryptedBytesCount = encryptionKey.EncryptData(compressedText, 0, compressedLength, cipherTextWithTypeMarker, 1);

                                if (encryptedBytesCount < 0)
                                {
                                    throw new InvalidOperationException($"{nameof(Encryptor)} returned null cipherText from {nameof(EncryptAsync)}.");
                                }

                                itemJObj[propertyName] = JsonValue.Create(cipherTextWithTypeMarker.AsSpan(0, encryptedBytesCount + 1).ToArray());

                                pathsCompressed.Add(propertyName, plainTextLength);
                                compressedProperties = true;
                            }
                            else
                            {
                                int cipherTextLength = encryptionKey.GetEncryptByteCount(plainTextLength);

                                byte[] cipherTextWithTypeMarker = arrayPoolManager.Rent(cipherTextLength + 1);

                                cipherTextWithTypeMarker[0] = (byte)typeMarker;

                                int encryptedBytesCount = encryptionKey.EncryptData(
                                    plainText,
                                    plainTextOffset: 0,
                                    plainTextLength,
                                    cipherTextWithTypeMarker,
                                    outputOffset: 1);

                                if (encryptedBytesCount < 0)
                                {
                                    throw new InvalidOperationException($"{nameof(Encryptor)} returned null cipherText from {nameof(EncryptAsync)}.");
                                }

                                itemJObj[propertyName] = JsonValue.Create(cipherTextWithTypeMarker.AsSpan(0, encryptedBytesCount + 1).ToArray());
                                pathsEncrypted.Add(pathToEncrypt);
                            }
                        }

                        encryptionProperties = new EncryptionProperties(
                                encryptionFormatVersion: compressedProperties ? 4 : 3,
                                encryptionOptions.EncryptionAlgorithm,
                                encryptionOptions.DataEncryptionKeyId,
                                encryptedData: null,
                                pathsEncrypted,
                                encryptionOptions.CompressionOptions.Algorithm,
                                compressedProperties ? pathsCompressed : null);

                        JsonNode node = System.Text.Json.JsonSerializer.SerializeToNode(encryptionProperties);

                        itemJObj.Add(Constants.EncryptedInfo, node);
                        input.Dispose();

                        MemoryStream ms = new MemoryStream();
                        using Utf8JsonWriter writer = new Utf8JsonWriter(ms);
                        System.Text.Json.JsonSerializer.Serialize(writer, document);
                        ms.Position = 0;
                        return ms;
                    }
                case CosmosEncryptionAlgorithm.AEAes256CbcHmacSha256Randomized:
                    {
                        JObject itemJObj = EncryptionProcessor.BaseSerializer.FromStream<JObject>(input);
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
                                encryptionOptions.PathsToEncrypt,
                                CompressionOptions.CompressionAlgorithm.None,
                                null);

                        itemJObj.Add(Constants.EncryptedInfo, JObject.FromObject(encryptionProperties));
                        input.Dispose();
                        return EncryptionProcessor.BaseSerializer.ToStream(itemJObj);
                    }
                default:
                    throw new NotSupportedException($"Encryption Algorithm : {encryptionOptions.EncryptionAlgorithm} is not supported.");
            }


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
                CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized when encryptionProperties.CompressionAlgorithm == CompressionOptions.CompressionAlgorithm.None => await EncryptionProcessor.MdeEncAlgoDecryptObjectAsync(
                    itemJObj,
                    encryptor,
                    encryptionProperties,
                    diagnosticsContext,
                    cancellationToken),
                CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized when encryptionProperties.CompressionAlgorithm != CompressionOptions.CompressionAlgorithm.None => await EncryptionProcessor.MdeEncAlgoDecryptCompressedObjectAsync(
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
            _ = diagnosticsContext;

            if (encryptionProperties.EncryptionFormatVersion != 3)
            {
                throw new NotSupportedException($"Unknown encryption format version: {encryptionProperties.EncryptionFormatVersion}. Please upgrade your SDK to the latest version.");
            }

            using ArrayPoolManager arrayPoolManager = new ArrayPoolManager();

            DataEncryptionKey encryptionKey = await encryptor.GetEncryptionKeyAsync(encryptionProperties.DataEncryptionKeyId, encryptionProperties.EncryptionAlgorithm, cancellationToken);

            List<string> pathsDecrypted = new List<string>(encryptionProperties.EncryptedPaths.Count());
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

                int plainTextLength = encryptionKey.GetDecryptByteCount(cipherTextWithTypeMarker.Length - 1);

                byte[] plainText = arrayPoolManager.Rent(plainTextLength);

                int decryptedCount = EncryptionProcessor.MdeEncAlgoDecryptPropertyAsync(
                    encryptionKey,
                    cipherTextWithTypeMarker,
                    cipherTextOffset: 1,
                    cipherTextWithTypeMarker.Length - 1,
                    plainText);

                EncryptionProcessor.DeserializeAndAddProperty(
                    (TypeMarker)cipherTextWithTypeMarker[0],
                    plainText.AsSpan(0, decryptedCount),
                    document,
                    propertyName);

                pathsDecrypted.Add(path);
            }

            DecryptionContext decryptionContext = EncryptionProcessor.CreateDecryptionContext(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            document.Remove(Constants.EncryptedInfo);
            return decryptionContext;
        }

        private static async Task<DecryptionContext> MdeEncAlgoDecryptCompressedObjectAsync(
            JObject document,
            Encryptor encryptor,
            EncryptionProperties encryptionProperties,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            _ = diagnosticsContext;

            if (encryptionProperties.EncryptionFormatVersion != 4)
            {
                throw new NotSupportedException($"Unknown encryption format version: {encryptionProperties.EncryptionFormatVersion}. Please upgrade your SDK to the latest version.");
            }

            using ArrayPoolManager arrayPoolManager = new ArrayPoolManager();
            CompressorFactory compressorFactory = new CompressorFactory(new CompressionOptions() {  Algorithm = encryptionProperties.CompressionAlgorithm });

            DataEncryptionKey encryptionKey = await encryptor.GetEncryptionKeyAsync(encryptionProperties.DataEncryptionKeyId, encryptionProperties.EncryptionAlgorithm, cancellationToken);

            List<string> pathsDecrypted = new List<string>(encryptionProperties.EncryptedPaths.Count());
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

                int compressedTextLength = encryptionKey.GetDecryptByteCount(cipherTextWithTypeMarker.Length - 1);

                byte[] compressedText = arrayPoolManager.Rent(compressedTextLength);

                int decryptedCount = EncryptionProcessor.MdeEncAlgoDecryptPropertyAsync(
                    encryptionKey,
                    cipherTextWithTypeMarker,
                    cipherTextOffset: 1,
                    cipherTextWithTypeMarker.Length - 1,
                    compressedText);

                EncryptionProcessor.DeserializeAndAddProperty(
                    (TypeMarker)cipherTextWithTypeMarker[0],
                    compressedText.AsSpan(0, decryptedCount),
                    document,
                    propertyName);

                pathsDecrypted.Add(path);
            }
            foreach (KeyValuePair<string, int> compressedPath in encryptionProperties.CompressedEncryptedPaths)
            {
                string propertyName = compressedPath.Key.Substring(1);
                if (!document.TryGetValue(propertyName, out JToken propertyValue))
                {
                    continue;
                }

                byte[] cipherTextWithTypeMarker = propertyValue.ToObject<byte[]>();
                if (cipherTextWithTypeMarker == null)
                {
                    continue;
                }

                int compressedTextLength = encryptionKey.GetDecryptByteCount(cipherTextWithTypeMarker.Length - 1);

                byte[] compressedText = arrayPoolManager.Rent(compressedTextLength);

                int decryptedCount = EncryptionProcessor.MdeEncAlgoDecryptPropertyAsync(
                    encryptionKey,
                    cipherTextWithTypeMarker,
                    cipherTextOffset: 1,
                    cipherTextWithTypeMarker.Length - 1,
                    compressedText);

                byte[] plainText = arrayPoolManager.Rent(compressedPath.Value);
                using MemoryStream ms = new MemoryStream(compressedText, 1, compressedTextLength - 1);
                using Stream decompressionStream = compressorFactory.CreateDecompressionStream(ms);
                await decompressionStream.ReadAsync(plainText, 0, compressedPath.Value);
                await decompressionStream.FlushAsync();

                EncryptionProcessor.DeserializeAndAddProperty(
                    (TypeMarker)cipherTextWithTypeMarker[0],
                    plainText.AsSpan(0, compressedPath.Value),
                    document,
                    propertyName);

                pathsDecrypted.Add(compressedPath.Key);
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

        private static int MdeEncAlgoDecryptPropertyAsync(
            DataEncryptionKey encryptionKey,
            byte[] cipherText,
            int cipherTextOffset,
            int cipherTextLength,
            byte[] buffer)
        {
            int decryptedCount = encryptionKey.DecryptData(
                cipherText,
                cipherTextOffset,
                cipherTextLength,
                buffer,
                outputOffset: 0);

            if (decryptedCount < 0)
            {
                throw new InvalidOperationException($"{nameof(Encryptor)} returned null plainText from {nameof(DecryptAsync)}.");
            }

            return decryptedCount;
        }

        private static async Task<DecryptionContext> LegacyEncAlgoDecryptContentAsync(
            JObject document,
            EncryptionProperties encryptionProperties,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            _ = diagnosticsContext;

            if (encryptionProperties.EncryptionFormatVersion != 2)
            {
                throw new NotSupportedException($"Unknown encryption format version: {encryptionProperties.EncryptionFormatVersion}. Please upgrade your SDK to the latest version.");
            }

            byte[] plainText = await encryptor.DecryptAsync(
                encryptionProperties.EncryptedData,
                encryptionProperties.DataEncryptionKeyId,
                encryptionProperties.EncryptionAlgorithm,
                cancellationToken) ?? throw new InvalidOperationException($"{nameof(Encryptor)} returned null plainText from {nameof(DecryptAsync)}.");
            JObject plainTextJObj;
            using (MemoryStream memoryStream = new MemoryStream(plainText))
            using (StreamReader streamReader = new StreamReader(memoryStream))
            using (JsonTextReader jsonTextReader = new JsonTextReader(streamReader))
            {
                jsonTextReader.ArrayPool = JsonArrayPool.Instance;
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
                jsonTextReader.ArrayPool = JsonArrayPool.Instance;
                JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64, // https://github.com/advisories/GHSA-5crp-9r3c-p9vr
                };

                itemJObj = Newtonsoft.Json.JsonSerializer.Create(jsonSerializerSettings).Deserialize<JObject>(jsonTextReader);
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

        private static (TypeMarker typeMarker, byte[] serializedBytes, int serializedBytesCount) Serialize(JsonNode propertyValue, ArrayPoolManager arrayPoolManager)
        {
            byte[] buffer;
            int length;
            switch (propertyValue.GetValueKind())
            {
                case JsonValueKind.Undefined:
                    Debug.Assert(false, "Undefined value cannot be in the JSON");
                    return (default, null, -1);
                case JsonValueKind.Null:
                    Debug.Assert(false, "Null type should have been handled by caller");
                    return (TypeMarker.Null, null, -1);
                case JsonValueKind.True:
                    (buffer, length) = SerializeFixed(SqlBoolSerializer, true);
                    return (TypeMarker.Boolean, buffer, length);
                case JsonValueKind.False:
                    (buffer, length) = SerializeFixed(SqlBoolSerializer, false);
                    return (TypeMarker.Boolean, buffer, length);
                case JsonValueKind.Number:
                    if (propertyValue.AsValue().TryGetValue(out long value))
                    {
                        (buffer, length) = SerializeFixed(SqlLongSerializer, value);
                        return (TypeMarker.Long, buffer, length);
                    }
                    else
                    {
                        (buffer, length) = SerializeFixed(SqlDoubleSerializer, propertyValue.GetValue<double>());
                        return (TypeMarker.Double, buffer, length);
                    }

                case JsonValueKind.String:
                    (buffer, length) = SerializeString(propertyValue.GetValue<string>());
                    return (TypeMarker.String, buffer, length);
                case JsonValueKind.Array:
                    (buffer, length) = SerializeString(propertyValue.ToJsonString());
                    return (TypeMarker.Array, buffer, length);
                case JsonValueKind.Object:
                    (buffer, length) = SerializeString(propertyValue.ToJsonString());
                    return (TypeMarker.Object, buffer, length);
                default:
                    throw new InvalidOperationException($" Invalid or Unsupported Data Type Passed : {propertyValue.GetValueKind()}");
            }

            (byte[], int) SerializeFixed<T>(IFixedSizeSerializer<T> serializer, T value)
            {
                byte[] buffer = arrayPoolManager.Rent(serializer.GetSerializedMaxByteCount());
                int length = serializer.Serialize(value, buffer);
                return (buffer, length);
            }

            (byte[], int) SerializeString(string value)
            {
                byte[] buffer = arrayPoolManager.Rent(SqlVarCharSerializer.GetSerializedMaxByteCount(value.Length));
                int length = SqlVarCharSerializer.Serialize(value, buffer);
                return (buffer, length);
            }
        }

        private static void DeserializeAndAddProperty(
            TypeMarker typeMarker,
            ReadOnlySpan<byte> serializedBytes,
            JObject jObject,
            string key)
        {
            switch (typeMarker)
            {
                case TypeMarker.Boolean:
                    jObject[key] = SqlBoolSerializer.Deserialize(serializedBytes);
                    break;
                case TypeMarker.Double:
                    jObject[key] = SqlDoubleSerializer.Deserialize(serializedBytes);
                    break;
                case TypeMarker.Long:
                    jObject[key] = SqlLongSerializer.Deserialize(serializedBytes);
                    break;
                case TypeMarker.String:
                    jObject[key] = SqlVarCharSerializer.Deserialize(serializedBytes);
                    break;
                case TypeMarker.Array:
                    DeserializeAndAddProperty<JArray>(serializedBytes);
                    break;
                case TypeMarker.Object:
                    DeserializeAndAddProperty<JObject>(serializedBytes);
                    break;
                default:
                    Debug.Fail(string.Format("Unexpected type marker {0}", typeMarker));
                    break;
            }

            void DeserializeAndAddProperty<T>(ReadOnlySpan<byte> serializedBytes)
                where T : JToken
            {
                using ArrayPoolManager<char> manager = new ArrayPoolManager<char>();

                char[] buffer = manager.Rent(SqlVarCharSerializer.GetDeserializedMaxLength(serializedBytes.Length));
                int length = SqlVarCharSerializer.Deserialize(serializedBytes, buffer.AsSpan());

                Newtonsoft.Json.JsonSerializer serializer = Newtonsoft.Json.JsonSerializer.Create(JsonSerializerSettings);

                using MemoryTextReader memoryTextReader = new MemoryTextReader(new Memory<char>(buffer, 0, length));
                using JsonTextReader reader = new JsonTextReader(memoryTextReader);

                jObject[key] = serializer.Deserialize<T>(reader);
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

        internal static int GetOriginalBase64Length(string base64string)
        {
            if (string.IsNullOrEmpty(base64string))
            {
                return 0;
            }

            int paddingCount = 0;
            int characterCount = base64string.Length;
            if (base64string[characterCount - 1] == '=')
            {
                paddingCount++;
            }

            if (base64string[characterCount - 2] == '=')
            {
                paddingCount++;
            }

            return (3 * (characterCount / 4)) - paddingCount;
        }
    }
}
