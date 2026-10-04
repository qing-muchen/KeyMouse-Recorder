using System.Diagnostics.CodeAnalysis;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models.Input;

namespace MacroRecorder.Core.Playback;

/// <summary>Tracks successful down/up injections for one playback and synthesizes safe releases.</summary>
internal sealed class PressedInputTracker
{
    private readonly List<PressedInput> pressedInputs = [];
    private int lastMouseX;
    private int lastMouseY;

    public void Track(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case KeyboardInputEvent keyboardEvent:
                TrackKeyboard(keyboardEvent);
                break;
            case MouseInputEvent mouseEvent:
                lastMouseX = mouseEvent.X;
                lastMouseY = mouseEvent.Y;
                TrackMouse(mouseEvent);
                break;
        }
    }

    public InputCleanupResult Clear()
    {
        pressedInputs.Clear();
        return InputCleanupResult.Empty;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every held input must receive a release attempt even when another release fails.")]
    public InputCleanupResult ReleaseAll(IInputInjector inputInjector)
    {
        var inputsToRelease = pressedInputs.ToArray();
        pressedInputs.Clear();
        var releasedInputCount = 0;
        List<Exception> failures = [];

        for (var index = inputsToRelease.Length - 1; index >= 0; index--)
        {
            try
            {
                inputInjector.Inject(inputsToRelease[index].CreateReleaseEvent(lastMouseX, lastMouseY));
                releasedInputCount++;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return new InputCleanupResult(releasedInputCount, failures);
    }

    private void TrackKeyboard(KeyboardInputEvent inputEvent)
    {
        var pressedIndex = FindPressedKeyIndex(inputEvent.VirtualKey, inputEvent.ScanCode);
        if (inputEvent.EventType == InputEventType.KeyboardKeyDown)
        {
            if (pressedIndex < 0)
            {
                pressedInputs.Add(PressedInput.ForKey(inputEvent));
            }
        }
        else if (pressedIndex >= 0)
        {
            pressedInputs.RemoveAt(pressedIndex);
        }
    }

    private void TrackMouse(MouseInputEvent inputEvent)
    {
        if (inputEvent.EventType is not (InputEventType.MouseButtonDown or InputEventType.MouseButtonUp))
        {
            return;
        }

        var pressedIndex = FindPressedMouseButtonIndex(inputEvent.Button);
        if (inputEvent.EventType == InputEventType.MouseButtonDown)
        {
            if (pressedIndex < 0)
            {
                pressedInputs.Add(PressedInput.ForMouseButton(inputEvent));
            }
        }
        else if (pressedIndex >= 0)
        {
            pressedInputs.RemoveAt(pressedIndex);
        }
    }

    private int FindPressedKeyIndex(uint virtualKey, uint scanCode)
    {
        for (var index = 0; index < pressedInputs.Count; index++)
        {
            var pressed = pressedInputs[index];
            if (pressed.Kind == PressedInputKind.Keyboard &&
                pressed.VirtualKey == virtualKey &&
                pressed.ScanCode == scanCode)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindPressedMouseButtonIndex(MouseButton button)
    {
        for (var index = 0; index < pressedInputs.Count; index++)
        {
            var pressed = pressedInputs[index];
            if (pressed.Kind == PressedInputKind.MouseButton && pressed.MouseButton == button)
            {
                return index;
            }
        }

        return -1;
    }

    private enum PressedInputKind
    {
        Keyboard,
        MouseButton,
    }

    private readonly record struct PressedInput(
        PressedInputKind Kind,
        long TimestampUs,
        uint VirtualKey,
        uint ScanCode,
        uint Flags,
        MouseButton MouseButton)
    {
        public static PressedInput ForKey(KeyboardInputEvent inputEvent) =>
            new(
                PressedInputKind.Keyboard,
                inputEvent.TimestampUs,
                inputEvent.VirtualKey,
                inputEvent.ScanCode,
                inputEvent.Flags,
                MouseButton.None);

        public static PressedInput ForMouseButton(MouseInputEvent inputEvent) =>
            new(
                PressedInputKind.MouseButton,
                inputEvent.TimestampUs,
                0,
                0,
                0,
                inputEvent.Button);

        public InputEvent CreateReleaseEvent(int mouseX, int mouseY) => Kind switch
        {
            PressedInputKind.Keyboard => new KeyboardInputEvent(
                TimestampUs,
                InputEventType.KeyboardKeyUp,
                VirtualKey,
                ScanCode,
                Flags),
            PressedInputKind.MouseButton => new MouseInputEvent(
                TimestampUs,
                InputEventType.MouseButtonUp,
                mouseX,
                mouseY,
                MouseButton,
                0),
            _ => throw new InvalidOperationException("Unsupported pressed input kind."),
        };
    }
}

internal sealed record InputCleanupResult(int ReleasedInputCount, IReadOnlyCollection<Exception> Failures)
{
    public static InputCleanupResult Empty { get; } = new(0, Array.Empty<Exception>());
}
