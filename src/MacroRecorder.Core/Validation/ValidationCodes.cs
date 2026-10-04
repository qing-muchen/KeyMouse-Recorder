namespace MacroRecorder.Core.Validation;

/// <summary>Stable machine-readable identifiers for Macro validation issues.</summary>
public static class ValidationCodes
{
    public const string SchemaVersionInvalid = "SCHEMA_VERSION_INVALID";
    public const string SchemaVersionUnsupported = "SCHEMA_VERSION_UNSUPPORTED";
    public const string MacroIdEmpty = "MACRO_ID_EMPTY";
    public const string MacroNameEmpty = "MACRO_NAME_EMPTY";
    public const string RecordingDurationNegative = "RECORDING_DURATION_NEGATIVE";
    public const string EventCountNegative = "EVENT_COUNT_NEGATIVE";
    public const string EventCountMismatch = "EVENT_COUNT_MISMATCH";
    public const string DurationBeforeLastEvent = "DURATION_BEFORE_LAST_EVENT";
    public const string EventsUninitialized = "EVENTS_UNINITIALIZED";
    public const string EmptyMacro = "EMPTY_MACRO";
    public const string EventNull = "EVENT_NULL";
    public const string TimestampNegative = "TIMESTAMP_NEGATIVE";
    public const string TimestampDecreasing = "TIMESTAMP_DECREASING";
    public const string EventTypeUnsupported = "EVENT_TYPE_UNSUPPORTED";
    public const string KeyboardEventTypeInvalid = "KEYBOARD_EVENT_TYPE_INVALID";
    public const string MouseEventTypeInvalid = "MOUSE_EVENT_TYPE_INVALID";
    public const string MouseButtonRequired = "MOUSE_BUTTON_REQUIRED";
    public const string MouseButtonMustBeNone = "MOUSE_BUTTON_MUST_BE_NONE";
    public const string MouseMoveWheelDeltaInvalid = "MOUSE_MOVE_WHEEL_DELTA_INVALID";
    public const string MouseButtonWheelDeltaInvalid = "MOUSE_BUTTON_WHEEL_DELTA_INVALID";
    public const string WheelDeltaZero = "WHEEL_DELTA_ZERO";
    public const string ScreenWidthInvalid = "SCREEN_WIDTH_INVALID";
    public const string ScreenHeightInvalid = "SCREEN_HEIGHT_INVALID";
    public const string DpiScaleInvalid = "DPI_SCALE_INVALID";
    public const string PlaybackSpeedInvalid = "PLAYBACK_SPEED_INVALID";
    public const string UpdatedAtBeforeCreatedAt = "UPDATED_AT_BEFORE_CREATED_AT";
}
