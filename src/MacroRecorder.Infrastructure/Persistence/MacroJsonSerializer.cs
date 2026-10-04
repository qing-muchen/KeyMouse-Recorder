using System.Text.Json;
using System.Text.Json.Serialization;
using MacroRecorder.Core.Models;

namespace MacroRecorder.Infrastructure.Persistence;

/// <summary>Implements the stable schema-v1 JSON contract over UTF-8 streams.</summary>
public sealed class MacroJsonSerializer : IMacroSerializer
{
    internal static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public async ValueTask SerializeAsync(
        Stream destination,
        Macro macro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(macro);

        await JsonSerializer.SerializeAsync(destination, macro, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<Macro> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var macro = await JsonSerializer.DeserializeAsync<Macro>(source, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return macro ?? throw new JsonException("The JSON document must contain a Macro object.");
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
