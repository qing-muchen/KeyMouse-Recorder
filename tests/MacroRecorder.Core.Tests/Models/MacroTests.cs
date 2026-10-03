using System.Collections.Immutable;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Schema;

namespace MacroRecorder.Core.Tests.Models;

public sealed class MacroTests
{
    [Fact]
    public void MacroConstructionContainsAllMetadataAndOrderedMixedEvents()
    {
        var macro = DomainTestData.CreateMacro();

        Assert.Equal(MacroSchema.CurrentVersion, macro.SchemaVersion);
        Assert.Equal(Guid.Parse("12acfcbb-743c-45a1-a136-a206aa2741f1"), macro.Id);
        Assert.Equal("Domain example", macro.Name);
        Assert.Equal("Mixed events with a negative screen coordinate", macro.Description);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), macro.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 1, 0, TimeSpan.Zero), macro.UpdatedAt);
        Assert.Equal(500_000L, macro.Recording.DurationUs);
        Assert.Equal(7, macro.Recording.EventCount);
        Assert.Equal(1920, macro.Environment.ScreenWidth);
        Assert.Equal(1080, macro.Environment.ScreenHeight);
        Assert.Equal(1.25, macro.Environment.DpiScale);
        Assert.Equal(1.5, macro.Playback.DefaultSpeed);
        Assert.Equal(
            [InputEventType.KeyboardKeyDown, InputEventType.KeyboardKeyUp, InputEventType.MouseMove,
                InputEventType.MouseButtonDown, InputEventType.MouseButtonUp,
                InputEventType.MouseVerticalWheel, InputEventType.MouseHorizontalWheel],
            macro.Events.Select(input => input.EventType));
    }

    [Fact]
    public void PlaybackPreferenceDefaultsToNormalSpeed()
    {
        Assert.Equal(1.0, new PlaybackMetadata().DefaultSpeed);
    }

    [Fact]
    public void EventsAreAnImmutableSnapshotOfTheSuppliedSource()
    {
        var first = new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0);
        var second = new KeyboardInputEvent(10, InputEventType.KeyboardKeyUp, 65, 30, 128);
        var source = new List<InputEvent> { first };
        var macro = DomainTestData.CreateMacro(source.ToImmutableArray());

        source[0] = second;
        source.Clear();
        var extended = macro.Events.Add(second);

        Assert.Same(first, Assert.Single(macro.Events));
        Assert.Equal(2, extended.Length);
        IList<InputEvent> mutableView = macro.Events;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = second);
        Assert.Throws<NotSupportedException>(() => mutableView.Add(second));
        Assert.Same(first, Assert.Single(macro.Events));
    }

    [Fact]
    public void EditingACopyDoesNotMutateTheOriginalDefinition()
    {
        var original = DomainTestData.CreateMacro();
        var edited = original with
        {
            Name = "Edited copy",
            Playback = original.Playback with { DefaultSpeed = 2.0 },
            Events = original.Events.RemoveAt(0),
        };

        Assert.Equal("Domain example", original.Name);
        Assert.Equal(1.5, original.Playback.DefaultSpeed);
        Assert.Equal(7, original.Events.Length);
        Assert.Equal("Edited copy", edited.Name);
        Assert.Equal(2.0, edited.Playback.DefaultSpeed);
        Assert.Equal(6, edited.Events.Length);
    }
}
