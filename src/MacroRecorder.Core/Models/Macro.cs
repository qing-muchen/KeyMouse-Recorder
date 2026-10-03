using System.Collections.Immutable;
using System.Text.Json.Serialization;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Schema;

namespace MacroRecorder.Core.Models;

/// <summary>
/// A reusable macro definition. Session state and file IO do not belong here.
/// Callers supply metadata timestamps, preferably in UTC; the model does not read a clock.
/// </summary>
public sealed record Macro
{
    /// <summary>
    /// Identifies the persisted schema, independently of assembly versions.
    /// Required in JSON even though new definitions default to the current version.
    /// </summary>
    [JsonRequired]
    public int SchemaVersion { get; init; } = MacroSchema.CurrentVersion;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required RecordingMetadata Recording { get; init; }
    public required EnvironmentMetadata Environment { get; init; }
    public required PlaybackMetadata Playback { get; init; }

    /// <summary>
    /// Immutable ordered events. Use an empty array for an empty definition, not default(ImmutableArray).
    /// Array operations return new collections; they cannot mutate this definition.
    /// </summary>
    public required ImmutableArray<InputEvent> Events { get; init; }
}
