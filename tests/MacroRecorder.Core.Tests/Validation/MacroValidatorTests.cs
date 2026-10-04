using System.Collections.Immutable;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Schema;
using MacroRecorder.Core.Tests.Models;
using MacroRecorder.Core.Validation;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.Validation;

public sealed class MacroValidatorTests
{

    [Fact]
    public void ValidMacroHasNoIssuesAndIsValid()
    {
        var result = MacroValidator.Validate(CreateValidMacro());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void EmptyMacroProducesWarningAndRemainsValid()
    {
        var macro = WithEvents(CreateValidMacro(), []);

        var result = MacroValidator.Validate(macro);

        var issue = Assert.Single(result.Issues);
        AssertIssue(issue, ValidationSeverity.Warning, ValidationCodes.EmptyMacro, "events");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyMacroMayHavePositiveDuration()
    {
        var macro = WithEvents(CreateValidMacro(), []) with
        {
            Recording = new RecordingMetadata(1_000_000, 0),
        };

        var result = MacroValidator.Validate(macro);

        Assert.True(result.IsValid);
        Assert.Equal(ValidationCodes.EmptyMacro, Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void EmptyEventsWithDeclaredCountReportsErrorAndWarning()
    {
        var macro = WithEvents(CreateValidMacro(), []) with
        {
            Recording = new RecordingMetadata(0, 1),
        };

        var result = MacroValidator.Validate(macro);

        Assert.False(result.IsValid);
        Assert.Equal(
            [ValidationCodes.EventCountMismatch, ValidationCodes.EmptyMacro],
            result.Issues.Select(static issue => issue.Code));
        Assert.Equal(ValidationSeverity.Error, result.Issues[0].Severity);
        Assert.Equal(ValidationSeverity.Warning, result.Issues[1].Severity);
    }

    [Fact]
    public void MultipleIssuesAreCollectedInStableRuleAndEventOrder()
    {
        ImmutableArray<InputEvent> events =
        [
            new KeyboardInputEvent(100, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new MouseInputEvent(50, InputEventType.MouseMove, -500, -200, MouseButton.Left, 4),
        ];
        var macro = CreateValidMacro() with
        {
            SchemaVersion = MacroSchema.CurrentVersion + 1,
            Id = Guid.Empty,
            Name = " ",
            CreatedAt = new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 10, 4, 1, 0, 0, TimeSpan.Zero),
            Recording = new RecordingMetadata(20, 99),
            Environment = new EnvironmentMetadata(0, -1, double.NaN),
            Playback = new PlaybackMetadata(double.PositiveInfinity),
            Events = events,
        };

        var result = MacroValidator.Validate(macro);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                ValidationCodes.SchemaVersionUnsupported,
                ValidationCodes.MacroIdEmpty,
                ValidationCodes.MacroNameEmpty,
                ValidationCodes.ScreenWidthInvalid,
                ValidationCodes.ScreenHeightInvalid,
                ValidationCodes.DpiScaleInvalid,
                ValidationCodes.PlaybackSpeedInvalid,
                ValidationCodes.UpdatedAtBeforeCreatedAt,
                ValidationCodes.EventCountMismatch,
                ValidationCodes.DurationBeforeLastEvent,
                ValidationCodes.TimestampDecreasing,
                ValidationCodes.MouseButtonMustBeNone,
                ValidationCodes.MouseMoveWheelDeltaInvalid,
            ],
            result.Issues.Select(static issue => issue.Code));
    }

    [Fact]
    public void RepeatedValidationProducesEquivalentOrderedResults()
    {
        var macro = CreateValidMacro() with
        {
            Id = Guid.Empty,
            Name = string.Empty,
            Playback = new PlaybackMetadata(0),
        };

        var first = MacroValidator.Validate(macro);
        var second = MacroValidator.Validate(macro);

        Assert.Equal(first.IsValid, second.IsValid);
        Assert.Equal(first.Issues.ToArray(), second.Issues.ToArray());
    }

    [Fact]
    public void ValidationDoesNotMutateMacroOrEvents()
    {
        var original = CreateValidMacro();
        var snapshot = original with { };
        var eventReferences = original.Events.ToArray();

        _ = MacroValidator.Validate(original);

        DomainTestData.AssertMacroEquivalent(snapshot, original);
        Assert.Equal(eventReferences, original.Events);
    }

    [Fact]
    public void NullMacroIsAProgrammerError()
    {
        Assert.Throws<ArgumentNullException>(() => MacroValidator.Validate(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveSchemaVersionIsInvalid(int schemaVersion)
    {
        var result = MacroValidator.Validate(CreateValidMacro() with { SchemaVersion = schemaVersion });

        AssertSingleError(result, ValidationCodes.SchemaVersionInvalid, "schemaVersion");
    }

    [Fact]
    public void FutureSchemaVersionIsUnsupported()
    {
        var result = MacroValidator.Validate(CreateValidMacro() with
        {
            SchemaVersion = MacroSchema.CurrentVersion + 1,
        });

        AssertSingleError(result, ValidationCodes.SchemaVersionUnsupported, "schemaVersion");
    }

    [Fact]
    public void EmptyIdIsInvalid()
    {
        var result = MacroValidator.Validate(CreateValidMacro() with { Id = Guid.Empty });

        AssertSingleError(result, ValidationCodes.MacroIdEmpty, "id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void EmptyOrWhitespaceNameIsInvalid(string name)
    {
        var result = MacroValidator.Validate(CreateValidMacro() with { Name = name });

        AssertSingleError(result, ValidationCodes.MacroNameEmpty, "name");
    }

    [Fact]
    public void NegativeRecordingDurationIsInvalid()
    {
        var macro = WithEvents(CreateValidMacro(), []) with
        {
            Recording = new RecordingMetadata(-1, 0),
        };

        var result = MacroValidator.Validate(macro);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, static issue =>
            issue.Code == ValidationCodes.RecordingDurationNegative
            && issue.Path == "recording.durationUs");
    }

    [Fact]
    public void NegativeEventCountIsInvalidAndMayAlsoMismatch()
    {
        var macro = CreateValidMacro() with
        {
            Recording = new RecordingMetadata(500_000, -1),
        };

        var result = MacroValidator.Validate(macro);

        Assert.False(result.IsValid);
        Assert.Equal(
            [ValidationCodes.EventCountNegative, ValidationCodes.EventCountMismatch],
            result.Issues.Select(static issue => issue.Code));
    }

    [Fact]
    public void DeclaredEventCountMustMatchActualCount()
    {
        var macro = CreateValidMacro() with
        {
            Recording = new RecordingMetadata(500_000, 99),
        };

        var result = MacroValidator.Validate(macro);

        AssertSingleError(result, ValidationCodes.EventCountMismatch, "recording.eventCount");
    }

    [Fact]
    public void DurationBeforeLastEventIsInvalid()
    {
        var events = ImmutableArray.Create<InputEvent>(
            new KeyboardInputEvent(1_000, InputEventType.KeyboardKeyDown, 65, 30, 0));
        var macro = WithEvents(CreateValidMacro(), events) with
        {
            Recording = new RecordingMetadata(999, 1),
        };

        var result = MacroValidator.Validate(macro);

        AssertSingleError(result, ValidationCodes.DurationBeforeLastEvent, "recording.durationUs");
    }

    [Fact]
    public void DurationEqualToLastEventIsValid()
    {
        var events = ImmutableArray.Create<InputEvent>(
            new KeyboardInputEvent(1_000, InputEventType.KeyboardKeyDown, 65, 30, 0));

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), events)).IsValid);
    }

    [Fact]
    public void DecreasingTimestampIsInvalidAtCurrentEventPath()
    {
        ImmutableArray<InputEvent> events =
        [
            new KeyboardInputEvent(1_000, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new KeyboardInputEvent(500, InputEventType.KeyboardKeyUp, 65, 30, 0),
        ];

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), events));

        AssertSingleError(result, ValidationCodes.TimestampDecreasing, "events[1].timestampUs");
    }

    [Fact]
    public void EveryAdjacentTimestampDecreaseIsReported()
    {
        ImmutableArray<InputEvent> events =
        [
            new KeyboardInputEvent(100, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new KeyboardInputEvent(50, InputEventType.KeyboardKeyUp, 65, 30, 0),
            new MouseInputEvent(40, InputEventType.MouseMove, 0, 0, MouseButton.None, 0),
        ];

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), events));

        Assert.Equal(2, result.Issues.Length);
        Assert.All(result.Issues, static issue => Assert.Equal(ValidationCodes.TimestampDecreasing, issue.Code));
        Assert.Equal("events[1].timestampUs", result.Issues[0].Path);
        Assert.Equal("events[2].timestampUs", result.Issues[1].Path);
    }

    [Fact]
    public void EqualTimestampsAreValidAndKeepCollectionOrder()
    {
        ImmutableArray<InputEvent> events =
        [
            new KeyboardInputEvent(100, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new MouseInputEvent(100, InputEventType.MouseButtonDown, 1, 2, MouseButton.Left, 0),
            new KeyboardInputEvent(100, InputEventType.KeyboardKeyUp, 65, 30, 0),
        ];
        var macro = WithEvents(CreateValidMacro(), events);

        var result = MacroValidator.Validate(macro);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Same(events[0], macro.Events[0]);
        Assert.Same(events[1], macro.Events[1]);
        Assert.Same(events[2], macro.Events[2]);
    }

    [Fact]
    public void IncreasingTimelineIsValid()
    {
        ImmutableArray<InputEvent> events =
        [
            new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0),
            new MouseInputEvent(1, InputEventType.MouseMove, 0, 0, MouseButton.None, 0),
            new KeyboardInputEvent(2, InputEventType.KeyboardKeyUp, 65, 30, 0),
        ];

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), events)).IsValid);
    }

    [Fact]
    public void LongMaxValueTimestampUsesComparisonWithoutOverflow()
    {
        var events = ImmutableArray.Create<InputEvent>(
            new KeyboardInputEvent(long.MaxValue, InputEventType.KeyboardKeyDown, 65, 30, 0));
        var macro = WithEvents(CreateValidMacro(), events);

        var result = MacroValidator.Validate(macro);

        Assert.True(result.IsValid);
        Assert.Equal(long.MaxValue, macro.Recording.DurationUs);
    }

    [Fact]
    public void DomainConstructorsRejectNegativeTimestampAndCrossFamilyEventTypes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KeyboardInputEvent(-1, InputEventType.KeyboardKeyDown, 65, 30, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KeyboardInputEvent(0, InputEventType.MouseMove, 65, 30, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MouseInputEvent(0, InputEventType.KeyboardKeyDown, 0, 0, MouseButton.None, 0));
    }

    [Theory]
    [InlineData(InputEventType.KeyboardKeyDown)]
    [InlineData(InputEventType.KeyboardKeyUp)]
    public void KeyboardTransitionsAreValid(InputEventType eventType)
    {
        var inputEvent = new KeyboardInputEvent(0, eventType, 0, 0, uint.MaxValue);

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent])).IsValid);
    }

    [Fact]
    public void UninitializedEventsCollectionIsInvalid()
    {
        var macro = CreateValidMacro() with { Events = default };

        var result = MacroValidator.Validate(macro);

        AssertSingleError(result, ValidationCodes.EventsUninitialized, "events");
    }

    [Fact]
    public void NullEventIsReportedWithoutStoppingOtherEventChecks()
    {
        ImmutableArray<InputEvent> events =
        [
            null!,
            new MouseInputEvent(10, InputEventType.MouseMove, 0, 0, MouseButton.Left, 1),
        ];
        var macro = CreateValidMacro() with
        {
            Events = events,
            Recording = new RecordingMetadata(10, 2),
        };

        var result = MacroValidator.Validate(macro);

        Assert.Equal(
            [ValidationCodes.EventNull, ValidationCodes.MouseButtonMustBeNone,
                ValidationCodes.MouseMoveWheelDeltaInvalid],
            result.Issues.Select(static issue => issue.Code));
        Assert.Equal("events[0]", result.Issues[0].Path);
    }

    [Fact]
    public void UnknownRuntimeEventTypeIsReported()
    {
        var inputEvent = new TestInputEvent(0, InputEventType.KeyboardKeyDown);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.EventTypeUnsupported, "events[0]");
    }

    [Theory]
    [InlineData(-1920, 200)]
    [InlineData(200, -1080)]
    [InlineData(-1920, -1080)]
    public void NegativeMouseCoordinatesAreValid(int x, int y)
    {
        var inputEvent = new MouseInputEvent(0, InputEventType.MouseMove, x, y, MouseButton.None, 0);

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent])).IsValid);
    }

    [Fact]
    public void MouseMoveWithNoneAndZeroDeltaIsValid()
    {
        var inputEvent = new MouseInputEvent(0, InputEventType.MouseMove, 1, 2, MouseButton.None, 0);

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent])).IsValid);
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.XButton2)]
    public void MouseMoveWithButtonIsInvalid(MouseButton button)
    {
        var inputEvent = new MouseInputEvent(0, InputEventType.MouseMove, 1, 2, button, 0);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.MouseButtonMustBeNone, "events[0].button");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void MouseMoveWithWheelDeltaIsInvalid(int wheelDelta)
    {
        var inputEvent = new MouseInputEvent(0, InputEventType.MouseMove, 1, 2, MouseButton.None, wheelDelta);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.MouseMoveWheelDeltaInvalid, "events[0].wheelDelta");
    }

    [Theory]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Left)]
    [InlineData(InputEventType.MouseButtonUp, MouseButton.XButton2)]
    [InlineData(InputEventType.MouseButtonDown, MouseButton.Middle)]
    public void MouseButtonTransitionsWithPhysicalButtonAreValid(InputEventType eventType, MouseButton button)
    {
        var inputEvent = new MouseInputEvent(0, eventType, 1, 2, button, 0);

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent])).IsValid);
    }

    [Theory]
    [InlineData(InputEventType.MouseButtonDown)]
    [InlineData(InputEventType.MouseButtonUp)]
    public void MouseButtonTransitionRequiresButton(InputEventType eventType)
    {
        var inputEvent = new MouseInputEvent(0, eventType, 1, 2, MouseButton.None, 0);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.MouseButtonRequired, "events[0].button");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-120)]
    public void MouseButtonTransitionRequiresZeroWheelDelta(int wheelDelta)
    {
        var inputEvent = new MouseInputEvent(
            0,
            InputEventType.MouseButtonDown,
            1,
            2,
            MouseButton.Left,
            wheelDelta);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.MouseButtonWheelDeltaInvalid, "events[0].wheelDelta");
    }

    [Theory]
    [InlineData(InputEventType.MouseVerticalWheel, 120)]
    [InlineData(InputEventType.MouseVerticalWheel, -120)]
    [InlineData(InputEventType.MouseVerticalWheel, 60)]
    [InlineData(InputEventType.MouseVerticalWheel, -60)]
    [InlineData(InputEventType.MouseVerticalWheel, 15)]
    [InlineData(InputEventType.MouseHorizontalWheel, 120)]
    [InlineData(InputEventType.MouseHorizontalWheel, -15)]
    public void WheelAllowsAnyNonZeroSignedDelta(InputEventType eventType, int wheelDelta)
    {
        var inputEvent = new MouseInputEvent(0, eventType, -500, 200, MouseButton.None, wheelDelta);

        Assert.True(MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent])).IsValid);
    }

    [Theory]
    [InlineData(InputEventType.MouseVerticalWheel)]
    [InlineData(InputEventType.MouseHorizontalWheel)]
    public void WheelRejectsZeroDelta(InputEventType eventType)
    {
        var inputEvent = new MouseInputEvent(0, eventType, 1, 2, MouseButton.None, 0);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.WheelDeltaZero, "events[0].wheelDelta");
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.XButton1)]
    public void WheelRejectsButton(MouseButton button)
    {
        var inputEvent = new MouseInputEvent(0, InputEventType.MouseVerticalWheel, 1, 2, button, 120);

        var result = MacroValidator.Validate(WithEvents(CreateValidMacro(), [inputEvent]));

        AssertSingleError(result, ValidationCodes.MouseButtonMustBeNone, "events[0].button");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveScreenWidthIsInvalid(int width)
    {
        var macro = CreateValidMacro() with { Environment = new EnvironmentMetadata(width, 1080, 1) };

        AssertSingleError(
            MacroValidator.Validate(macro),
            ValidationCodes.ScreenWidthInvalid,
            "environment.screenWidth");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveScreenHeightIsInvalid(int height)
    {
        var macro = CreateValidMacro() with { Environment = new EnvironmentMetadata(1920, height, 1) };

        AssertSingleError(
            MacroValidator.Validate(macro),
            ValidationCodes.ScreenHeightInvalid,
            "environment.screenHeight");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DpiScaleMustBePositiveAndFinite(double dpiScale)
    {
        var macro = CreateValidMacro() with { Environment = new EnvironmentMetadata(1920, 1080, dpiScale) };

        AssertSingleError(MacroValidator.Validate(macro), ValidationCodes.DpiScaleInvalid, "environment.dpiScale");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PlaybackSpeedMustBePositiveAndFinite(double speed)
    {
        var macro = CreateValidMacro() with { Playback = new PlaybackMetadata(speed) };

        AssertSingleError(
            MacroValidator.Validate(macro),
            ValidationCodes.PlaybackSpeedInvalid,
            "playback.defaultSpeed");
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    [InlineData(100.0)]
    public void AnyPositiveFinitePlaybackSpeedIsValidInPhaseSeven(double speed)
    {
        var macro = CreateValidMacro() with { Playback = new PlaybackMetadata(speed) };

        Assert.True(MacroValidator.Validate(macro).IsValid);
    }

    [Fact]
    public void UpdatedBeforeCreatedIsWarningOnly()
    {
        var macro = CreateValidMacro() with
        {
            CreatedAt = new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 10, 4, 1, 0, 0, TimeSpan.Zero),
        };

        var result = MacroValidator.Validate(macro);

        Assert.True(result.IsValid);
        AssertIssue(
            Assert.Single(result.Issues),
            ValidationSeverity.Warning,
            ValidationCodes.UpdatedAtBeforeCreatedAt,
            "updatedAt");
    }

    [Fact]
    public void ErrorAndWarningTogetherAreInvalid()
    {
        var macro = CreateValidMacro() with
        {
            Name = string.Empty,
            CreatedAt = new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 10, 4, 1, 0, 0, TimeSpan.Zero),
        };

        var result = MacroValidator.Validate(macro);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, static issue => issue.Severity == ValidationSeverity.Error);
        Assert.Contains(result.Issues, static issue => issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task PersistenceLoadsUnsupportedSchemaBeforeValidatorRejectsIt()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "unsupported-schema.json");
        var stored = CreateValidMacro() with { SchemaVersion = MacroSchema.CurrentVersion + 1 };
        var store = new MacroFileStore();
        await store.SaveAsync(path, stored);

        var loaded = await store.LoadAsync(path);
        var result = MacroValidator.Validate(loaded);

        Assert.Equal(stored.SchemaVersion, loaded.SchemaVersion);
        AssertSingleError(result, ValidationCodes.SchemaVersionUnsupported, "schemaVersion");
    }

    [Fact]
    public void ValidationResultDerivesValidityAndKeepsImmutableIssues()
    {
        var warning = new ValidationIssue(ValidationSeverity.Warning, "WARNING", "Warning message.", "events");
        var error = new ValidationIssue(ValidationSeverity.Error, "ERROR", "Error message.", "name");
        var warningResult = new ValidationResult([warning]);
        var errorResult = new ValidationResult([warning, error]);

        Assert.True(warningResult.IsValid);
        Assert.False(errorResult.IsValid);
        Assert.IsType<ImmutableArray<ValidationIssue>>(warningResult.Issues);
        Assert.Throws<ArgumentException>(() => new ValidationResult(default));
    }

    private static Macro CreateValidMacro() => DomainTestData.CreateMacro();

    private static Macro WithEvents(Macro macro, ImmutableArray<InputEvent> events)
    {
        var duration = events.IsEmpty ? 0 : events[^1]?.TimestampUs ?? 0;
        return macro with
        {
            Recording = new RecordingMetadata(duration, events.Length),
            Events = events,
        };
    }

    private static void AssertSingleError(ValidationResult result, string code, string path)
    {
        Assert.False(result.IsValid);
        AssertIssue(Assert.Single(result.Issues), ValidationSeverity.Error, code, path);
    }

    private static void AssertIssue(
        ValidationIssue issue,
        ValidationSeverity severity,
        string code,
        string path)
    {
        Assert.Equal(severity, issue.Severity);
        Assert.Equal(code, issue.Code);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
        Assert.Equal(path, issue.Path);
    }

    private sealed record TestInputEvent(long Timestamp, InputEventType Type) : InputEvent(Timestamp, Type);
}
