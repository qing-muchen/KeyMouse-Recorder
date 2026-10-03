using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroRecorder.Core.Tests.Serialization;

/// <summary>In-memory schema v1 proof only. File serialization services belong to Phase 6.</summary>
internal static class DomainJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
}
