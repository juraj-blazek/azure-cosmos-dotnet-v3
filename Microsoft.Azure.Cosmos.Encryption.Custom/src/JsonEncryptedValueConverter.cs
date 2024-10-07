namespace Microsoft.Azure.Cosmos.Encryption.Custom
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    internal sealed class JsonEncryptedValueConverter : JsonConverter<JsonEncryptedValue>
    {
        public override JsonEncryptedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, JsonEncryptedValue value, JsonSerializerOptions options)
        {
            writer.WriteBase64StringValue(value.Value.AsSpan(value.Offset, value.Length));
        }
    }
}