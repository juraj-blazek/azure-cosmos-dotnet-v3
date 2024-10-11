// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

#if ENCRYPTION_CUSTOM_PREVIEW && NET8_0_OR_GREATER

namespace Microsoft.Azure.Cosmos.Encryption.Custom.Transformation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    internal class MdeJsonNodeEncryptionProcessor
    {
        private readonly JsonWriterOptions jsonWriterOptions = new () { SkipValidation = true };

        private readonly JsonReaderOptions jsonReaderOptions = new ()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        internal JsonNodeSqlSerializer Serializer { get; set; } = new JsonNodeSqlSerializer();

        internal MdeEncryptor Encryptor { get; set; } = new MdeEncryptor();

        public async Task<Stream> EncryptAsync(
            Stream input,
            Encryptor encryptor,
            EncryptionOptions encryptionOptions,
            CancellationToken token)
        {
            JsonNode itemJObj = JsonNode.Parse(input);

            Stream result = await this.EncryptAsync(itemJObj, encryptor, encryptionOptions, token);

            await input.DisposeAsync();
            return result;
        }

        public async Task<Stream> EncryptAsync(
            JsonNode document,
            Encryptor encryptor,
            EncryptionOptions encryptionOptions,
            CancellationToken token)
        {
            List<string> pathsEncrypted = new ();
            TypeMarker typeMarker;

            using ArrayPoolManager arrayPoolManager = new ();

            JsonObject itemObj = document.AsObject();

            DataEncryptionKey encryptionKey = await encryptor.GetEncryptionKeyAsync(encryptionOptions.DataEncryptionKeyId, encryptionOptions.EncryptionAlgorithm, token);

            foreach (string pathToEncrypt in encryptionOptions.PathsToEncrypt)
            {
                string propertyName = pathToEncrypt[1..];
                if (!itemObj.TryGetPropertyValue(propertyName, out JsonNode propertyValue))
                {
                    continue;
                }

                if (propertyValue == null || propertyValue.GetValueKind() == JsonValueKind.Null)
                {
                    continue;
                }

                byte[] plainText = null;
                (typeMarker, plainText, int plainTextLength) = this.Serializer.Serialize(propertyValue, arrayPoolManager);

                if (plainText == null)
                {
                    continue;
                }

                (byte[] encryptedBytes, int encryptedBytesCount) = this.Encryptor.Encrypt(encryptionKey, typeMarker, plainText, plainTextLength, arrayPoolManager);

                itemObj[propertyName] = JsonValue.Create(new Memory<byte>(encryptedBytes, 0, encryptedBytesCount));
                pathsEncrypted.Add(pathToEncrypt);
            }

            EncryptionProperties encryptionProperties = new (
                encryptionFormatVersion: 3,
                encryptionOptions.EncryptionAlgorithm,
                encryptionOptions.DataEncryptionKeyId,
                encryptedData: null,
                pathsEncrypted);

            JsonNode propertiesNode = JsonSerializer.SerializeToNode(encryptionProperties);

            itemObj.Add(Constants.EncryptedInfo, propertiesNode);

            MemoryStream ms = new ();
            Utf8JsonWriter writer = new (ms, this.jsonWriterOptions);

            JsonSerializer.Serialize(writer, document);

            ms.Position = 0;
            return ms;
        }

        internal async Task<DecryptionContext> DecryptObjectAsync(
            JsonNode document,
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

            using ArrayPoolManager arrayPoolManager = new ();

            DataEncryptionKey encryptionKey = await encryptor.GetEncryptionKeyAsync(encryptionProperties.DataEncryptionKeyId, encryptionProperties.EncryptionAlgorithm, cancellationToken);

            List<string> pathsDecrypted = new (encryptionProperties.EncryptedPaths.Count());

            JsonObject itemObj = document.AsObject();

            foreach (string path in encryptionProperties.EncryptedPaths)
            {
                string propertyName = path[1..];

                if (!itemObj.TryGetPropertyValue(propertyName, out JsonNode propertyValue))
                {
                    // malformed document, such record shouldn't be there at all
                    continue;
                }

                // can we get to internal JsonNode buffers to avoid string allocation here?
                string base64String = propertyValue.GetValue<string>();
                byte[] cipherTextWithTypeMarker = arrayPoolManager.Rent((base64String.Length * sizeof(char) * 3 / 4) + 4);
                if (!Convert.TryFromBase64Chars(base64String, cipherTextWithTypeMarker, out int cipherTextLength))
                {
                    continue;
                }

                (byte[] plainText, int decryptedCount) = this.Encryptor.Decrypt(encryptionKey, cipherTextWithTypeMarker, cipherTextLength, arrayPoolManager);

                document[propertyName] = this.Serializer.Deserialize(
                    (TypeMarker)cipherTextWithTypeMarker[0],
                    plainText.AsSpan(0, decryptedCount));

                pathsDecrypted.Add(path);
            }

            DecryptionContext decryptionContext = EncryptionProcessor.CreateDecryptionContext(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            itemObj.Remove(Constants.EncryptedInfo);
            return decryptionContext;
        }

        internal async Task<(JsonNode, DecryptionContext)> DecryptObjectAsync(
            Stream stream,
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

            using ArrayPoolManager arrayPoolManager = new ();

            DataEncryptionKey encryptionKey = await encryptor.GetEncryptionKeyAsync(encryptionProperties.DataEncryptionKeyId, encryptionProperties.EncryptionAlgorithm, cancellationToken);

            List<string> pathsDecrypted = new (encryptionProperties.EncryptedPaths.Count());

            JsonNode document = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonObject itemObj = document.AsObject();

            byte[] rawInput = null;
            int streamLength = (int)stream.Position; // even if stream doesn't support .Length we are already at the end after JsonNode.Parse
            stream.Position = 0;

            if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                rawInput = buffer.Array;
            }
            else
            {
                rawInput = arrayPoolManager.Rent(streamLength);
                using MemoryStream memstream = new (rawInput);
                await stream.CopyToAsync(memstream, cancellationToken);
            }

            DecryptionContext decryptionContext = this.DecryptObject(rawInput.AsSpan(0, streamLength), itemObj, encryptionKey, encryptionProperties, arrayPoolManager);

            await stream.DisposeAsync();

            return (document, decryptionContext);
        }

        private DecryptionContext DecryptObject(
            Span<byte> rawInput,
            JsonObject document,
            DataEncryptionKey encryptionKey,
            EncryptionProperties encryptionProperties,
            ArrayPoolManager arrayPoolManager)
        {
            Utf8JsonReader reader = new (rawInput, this.jsonReaderOptions);

            HashSet<string> encryptedPaths = new (encryptionProperties.EncryptedPaths, StringComparer.Ordinal);

            string propertyName = null;
            string pathName = null;

            int pathsToProcess = encryptedPaths.Count;
            List<string> pathsDecrypted = new (encryptedPaths.Count);

            while (pathsToProcess > 0 && reader.Read())
            {
                if (reader.CurrentDepth != 1)
                {
                    continue;
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        propertyName = reader.GetString();
                        pathName = "/" + propertyName;
                        if (!encryptionProperties.EncryptedPaths.Contains(pathName))
                        {
                            propertyName = null;
                        }

                        break;
                    case JsonTokenType.String:
                        if (propertyName == null)
                        {
                            break;
                        }

                        byte[] cipherTextWithTypeMarker = reader.GetBytesFromBase64();

                        (byte[] plainText, int decryptedCount) = this.Encryptor.Decrypt(encryptionKey, cipherTextWithTypeMarker, cipherTextWithTypeMarker.Length, arrayPoolManager);

                        document[propertyName] = this.Serializer.Deserialize(
                            (TypeMarker)cipherTextWithTypeMarker[0],
                            plainText.AsSpan(0, decryptedCount));

                        pathsDecrypted.Add(pathName);
                        pathsToProcess--;
                        break;
                }
            }

            DecryptionContext decryptionContext = EncryptionProcessor.CreateDecryptionContext(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            document.Remove(Constants.EncryptedInfo);

            return decryptionContext;
        }
    }
}

#endif