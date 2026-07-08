using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2Companion.Core.Export;

/// <summary>Parses a JSON file produced by <see cref="CharacterExporter.ToJson"/>.</summary>
public static class CharacterImporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Deserializes an export, or returns <c>null</c> if the text isn't valid export JSON.</summary>
    public static CharacterExport? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CharacterExport>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
