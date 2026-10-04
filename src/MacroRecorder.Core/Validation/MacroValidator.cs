using System.Collections.Immutable;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Schema;

namespace MacroRecorder.Core.Validation;

/// <summary>Performs deterministic, side-effect-free semantic validation of a Macro.</summary>
public static class MacroValidator
{
    public static ValidationResult Validate(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);

        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();

        ValidateSchema(macro, issues);
        ValidateIdentity(macro, issues);
        ValidateRecordingMetadata(macro, issues);
        ValidateEnvironment(macro, issues);
        ValidatePlayback(macro, issues);
        ValidateMetadataDates(macro, issues);
        ValidateEvents(macro, issues);

        return new ValidationResult(issues.ToImmutable());
    }

    private static void ValidateSchema(Macro macro, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (macro.SchemaVersion <= 0)
        {
            AddError(
                issues,
                ValidationCodes.SchemaVersionInvalid,
                "Schema version must be a positive integer.",
                "schemaVersion");
        }
        else if (macro.SchemaVersion != MacroSchema.CurrentVersion)
        {
            AddError(
                issues,
                ValidationCodes.SchemaVersionUnsupported,
                "Schema version is not supported by this application.",
                "schemaVersion");
        }
    }

    private static void ValidateIdentity(Macro macro, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (macro.Id == Guid.Empty)
        {
            AddError(issues, ValidationCodes.MacroIdEmpty, "Macro ID must not be empty.", "id");
        }

        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            AddError(issues, ValidationCodes.MacroNameEmpty, "Macro name must not be empty.", "name");
        }
    }

    private static void ValidateRecordingMetadata(
        Macro macro,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (macro.Recording.DurationUs < 0)
        {
            AddError(
                issues,
                ValidationCodes.RecordingDurationNegative,
                "Recording duration must not be negative.",
                "recording.durationUs");
        }

        if (macro.Recording.EventCount < 0)
        {
            AddError(
                issues,
                ValidationCodes.EventCountNegative,
                "Recorded event count must not be negative.",
                "recording.eventCount");
        }
    }

    private static void ValidateEnvironment(Macro macro, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (macro.Environment.ScreenWidth <= 0)
        {
            AddError(
                issues,
                ValidationCodes.ScreenWidthInvalid,
                "Recorded screen width must be greater than zero.",
                "environment.screenWidth");
        }

        if (macro.Environment.ScreenHeight <= 0)
        {
            AddError(
                issues,
                ValidationCodes.ScreenHeightInvalid,
                "Recorded screen height must be greater than zero.",
                "environment.screenHeight");
        }

        if (!double.IsFinite(macro.Environment.DpiScale) || macro.Environment.DpiScale <= 0)
        {
            AddError(
                issues,
                ValidationCodes.DpiScaleInvalid,
                "Recorded DPI scale must be a finite value greater than zero.",
                "environment.dpiScale");
        }
    }

    private static void ValidatePlayback(Macro macro, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (!double.IsFinite(macro.Playback.DefaultSpeed) || macro.Playback.DefaultSpeed <= 0)
        {
            AddError(
                issues,
                ValidationCodes.PlaybackSpeedInvalid,
                "Default playback speed must be a finite value greater than zero.",
                "playback.defaultSpeed");
        }
    }

    private static void ValidateMetadataDates(Macro macro, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (macro.UpdatedAt < macro.CreatedAt)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                ValidationCodes.UpdatedAtBeforeCreatedAt,
                "Updated time is earlier than created time.",
                "updatedAt"));
        }
    }

    private static void ValidateEvents(Macro macro, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (macro.Events.IsDefault)
        {
            AddError(
                issues,
                ValidationCodes.EventsUninitialized,
                "Events must be an initialized collection.",
                "events");
            return;
        }

        if (macro.Recording.EventCount != macro.Events.Length)
        {
            AddError(
                issues,
                ValidationCodes.EventCountMismatch,
                "Recorded event count must equal the number of events.",
                "recording.eventCount");
        }

        if (macro.Events.IsEmpty)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                ValidationCodes.EmptyMacro,
                "Macro contains no input events.",
                "events"));
            return;
        }

        var lastEvent = macro.Events[^1];
        if (lastEvent is not null && macro.Recording.DurationUs < lastEvent.TimestampUs)
        {
            AddError(
                issues,
                ValidationCodes.DurationBeforeLastEvent,
                "Recording duration must not be earlier than the last event.",
                "recording.durationUs");
        }

        long? previousTimestamp = null;
        for (var index = 0; index < macro.Events.Length; index++)
        {
            var inputEvent = macro.Events[index];
            if (inputEvent is null)
            {
                AddError(
                    issues,
                    ValidationCodes.EventNull,
                    "Input event must not be null.",
                    $"events[{index}]");
                continue;
            }

            ValidateTimestamp(inputEvent, index, previousTimestamp, issues);
            previousTimestamp = inputEvent.TimestampUs;
            ValidateEvent(inputEvent, index, issues);
        }
    }

    private static void ValidateTimestamp(
        InputEvent inputEvent,
        int index,
        long? previousTimestamp,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        var path = $"events[{index}].timestampUs";
        if (inputEvent.TimestampUs < 0)
        {
            AddError(issues, ValidationCodes.TimestampNegative, "Event timestamp must not be negative.", path);
        }

        if (previousTimestamp.HasValue && inputEvent.TimestampUs < previousTimestamp.Value)
        {
            AddError(
                issues,
                ValidationCodes.TimestampDecreasing,
                "Event timestamp must not be earlier than the previous event.",
                path);
        }
    }

    private static void ValidateEvent(
        InputEvent inputEvent,
        int index,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        switch (inputEvent)
        {
            case KeyboardInputEvent keyboard:
                ValidateKeyboardEvent(keyboard, index, issues);
                break;
            case MouseInputEvent mouse:
                ValidateMouseEvent(mouse, index, issues);
                break;
            default:
                AddError(
                    issues,
                    ValidationCodes.EventTypeUnsupported,
                    "Input event runtime type is not supported.",
                    $"events[{index}]");
                break;
        }
    }

    private static void ValidateKeyboardEvent(
        KeyboardInputEvent keyboard,
        int index,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (keyboard.EventType is not (InputEventType.KeyboardKeyDown or InputEventType.KeyboardKeyUp))
        {
            AddError(
                issues,
                ValidationCodes.KeyboardEventTypeInvalid,
                "Keyboard event must use a keyboard event type.",
                $"events[{index}].eventType");
        }
    }

    private static void ValidateMouseEvent(
        MouseInputEvent mouse,
        int index,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        switch (mouse.EventType)
        {
            case InputEventType.MouseMove:
                ValidateMouseMove(mouse, index, issues);
                break;
            case InputEventType.MouseButtonDown:
            case InputEventType.MouseButtonUp:
                ValidateMouseButton(mouse, index, issues);
                break;
            case InputEventType.MouseVerticalWheel:
            case InputEventType.MouseHorizontalWheel:
                ValidateMouseWheel(mouse, index, issues);
                break;
            default:
                AddError(
                    issues,
                    ValidationCodes.MouseEventTypeInvalid,
                    "Mouse event must use a mouse event type.",
                    $"events[{index}].eventType");
                break;
        }
    }

    private static void ValidateMouseMove(
        MouseInputEvent mouse,
        int index,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (mouse.Button != MouseButton.None)
        {
            AddError(
                issues,
                ValidationCodes.MouseButtonMustBeNone,
                "Mouse move event must not specify a button.",
                $"events[{index}].button");
        }

        if (mouse.WheelDelta != 0)
        {
            AddError(
                issues,
                ValidationCodes.MouseMoveWheelDeltaInvalid,
                "Mouse move event must have a zero wheel delta.",
                $"events[{index}].wheelDelta");
        }
    }

    private static void ValidateMouseButton(
        MouseInputEvent mouse,
        int index,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (!IsPhysicalMouseButton(mouse.Button))
        {
            AddError(
                issues,
                ValidationCodes.MouseButtonRequired,
                "Mouse button event must specify a supported button.",
                $"events[{index}].button");
        }

        if (mouse.WheelDelta != 0)
        {
            AddError(
                issues,
                ValidationCodes.MouseButtonWheelDeltaInvalid,
                "Mouse button event must have a zero wheel delta.",
                $"events[{index}].wheelDelta");
        }
    }

    private static void ValidateMouseWheel(
        MouseInputEvent mouse,
        int index,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (mouse.Button != MouseButton.None)
        {
            AddError(
                issues,
                ValidationCodes.MouseButtonMustBeNone,
                "Mouse wheel event must not specify a button.",
                $"events[{index}].button");
        }

        if (mouse.WheelDelta == 0)
        {
            AddError(
                issues,
                ValidationCodes.WheelDeltaZero,
                "Mouse wheel event must have a non-zero wheel delta.",
                $"events[{index}].wheelDelta");
        }
    }

    private static bool IsPhysicalMouseButton(MouseButton button) =>
        button is MouseButton.Left or MouseButton.Right or MouseButton.Middle
            or MouseButton.XButton1 or MouseButton.XButton2;

    private static void AddError(
        ImmutableArray<ValidationIssue>.Builder issues,
        string code,
        string message,
        string path) =>
        issues.Add(new ValidationIssue(ValidationSeverity.Error, code, message, path));
}
