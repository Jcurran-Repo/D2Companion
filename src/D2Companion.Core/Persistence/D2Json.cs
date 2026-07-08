using System.Text.Json;
using System.Text.Json.Serialization;

namespace D2Companion.Core.Persistence;

/// <summary>Shared JSON settings so the aggregate serializes identically everywhere
/// (enums as readable strings, so the stored blob is human-inspectable).</summary>
internal static class D2Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
}
