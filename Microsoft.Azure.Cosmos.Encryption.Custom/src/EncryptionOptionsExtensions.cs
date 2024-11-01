// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal static class EncryptionOptionsExtensions
    {
        internal static void Validate(this EncryptionOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.DataEncryptionKeyId))
            {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                throw new ArgumentNullException(nameof(options.DataEncryptionKeyId));
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
            }

            if (string.IsNullOrWhiteSpace(options.EncryptionAlgorithm))
            {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                throw new ArgumentNullException(nameof(options.EncryptionAlgorithm));
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
            }

            if (options.PathsToEncrypt == null)
            {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                throw new ArgumentNullException(nameof(options.PathsToEncrypt));
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
            }

            if (options.PathsToEncrypt is not HashSet<string> && options.PathsToEncrypt.Distinct().Count() != options.PathsToEncrypt.Count())
            {
                throw new InvalidOperationException($"Duplicate paths in {nameof(options.PathsToEncrypt)}.");
            }

            foreach (string path in options.PathsToEncrypt)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    path[0] != '/' ||
                    path.IndexOf('/', 1) != -1 ||
                    path.AsSpan(1).Equals("id".AsSpan(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Invalid path '{path ?? string.Empty}' in {nameof(options.PathsToEncrypt)}.");
                }
            }

#if ENCRYPTION_CUSTOM_PREVIEW && NET8_0_OR_GREATER
            if (options.JsonProcessor == JsonProcessor.Stream && options.EncryptionAlgorithm != CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized)
            {
                throw new InvalidOperationException($"{nameof(JsonProcessor)}.{nameof(JsonProcessor.Stream)} can be used only with {CosmosEncryptionAlgorithm.MdeAeadAes256CbcHmac256Randomized} encryption algorithm.");
            }
#endif

            options.CompressionOptions?.Validate();
        }

        internal static void Validate(this CompressionOptions options)
        {
            if (options.MinimalCompressedLength < 0)
            {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                throw new ArgumentOutOfRangeException(nameof(options.MinimalCompressedLength));
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
            }
        }
    }
}
