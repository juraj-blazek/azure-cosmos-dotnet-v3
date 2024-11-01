// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

#if ENCRYPTION_CUSTOM_PREVIEW

namespace Microsoft.Azure.Cosmos.Encryption.Custom.Transformation
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json.Linq;

    internal class MdeEncryptionProcessor
    {
        private readonly MdeJObjectEncryptionProcessor jObjectEncryptionProcessor = new MdeJObjectEncryptionProcessor();

#if NET8_0_OR_GREATER
        private readonly StreamProcessor streamProcessor = new StreamProcessor();
#endif

        public async Task<Stream> EncryptAsync(
            Stream input,
            Encryptor encryptor,
            EncryptionOptions encryptionOptions,
            CancellationToken token)
        {
#if NET8_0_OR_GREATER
            switch (encryptionOptions.JsonProcessor)
            {
                case JsonProcessor.Newtonsoft:
                    return await this.jObjectEncryptionProcessor.EncryptAsync(input, encryptor, encryptionOptions, token);
                case JsonProcessor.Stream:
                    MemoryStream ms = new ();
                    await this.streamProcessor.EncryptStreamAsync(input, ms, encryptor, encryptionOptions, token);
                    return ms;

                default:
                    throw new InvalidOperationException("Unsupported JsonProcessor");
            }
#else
            return encryptionOptions.JsonProcessor switch
            {
                JsonProcessor.Newtonsoft => await this.jObjectEncryptionProcessor.EncryptAsync(input, encryptor, encryptionOptions, token),
                _ => throw new InvalidOperationException("Unsupported JsonProcessor"),
            };
#endif
        }

        internal async Task<DecryptionContext> DecryptObjectAsync(
            JObject document,
            Encryptor encryptor,
            EncryptionProperties encryptionProperties,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            return await this.jObjectEncryptionProcessor.DecryptObjectAsync(document, encryptor, encryptionProperties, diagnosticsContext, cancellationToken);
        }
    }
}
#endif