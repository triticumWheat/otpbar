using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtpBar.Core;

internal static class VaultJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new OtpEntryConverter() }
    };

    internal sealed record SavedVault(int SchemaVersion, IReadOnlyList<OtpEntry>? Entries);

    /// <summary>Reads through the validating constructor so a hand-edited file can never load as a usable entry.</summary>
    private sealed class OtpEntryConverter : JsonConverter<OtpEntry>
    {
        public override OtpEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var element = document.RootElement;
            if (!OtpAlgorithmNames.TryParse(element.GetProperty("algorithm").GetString()!, out var algorithm))
            {
                throw new JsonException();
            }
            return new OtpEntry(
                element.GetProperty("name").GetString()!,
                element.GetProperty("secret").GetString()!,
                id: element.GetProperty("id").GetGuid(),
                account: element.GetProperty("account").GetString()!,
                algorithm: algorithm,
                digits: element.GetProperty("digits").GetInt32(),
                period: element.GetProperty("period").GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, OtpEntry value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("name", value.Name);
            writer.WriteString("account", value.Account);
            writer.WriteString("secret", value.Secret);
            writer.WriteString("algorithm", value.Algorithm.ToName());
            writer.WriteNumber("digits", value.Digits);
            writer.WriteNumber("period", value.Period);
            writer.WriteEndObject();
        }
    }
}
