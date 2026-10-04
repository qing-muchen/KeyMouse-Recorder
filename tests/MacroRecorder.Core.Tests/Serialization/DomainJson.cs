using System.Text.Json;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.Serialization;

/// <summary>Phase 1 contract tests use the same frozen options as the production serializer.</summary>
internal static class DomainJson
{
    public static JsonSerializerOptions Options => MacroJsonSerializer.SerializerOptions;
}
