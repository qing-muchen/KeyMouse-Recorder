using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroRecorder.Core.InputInjection;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Infrastructure.Windows.Input;

namespace MacroRecorder.Core.Tests.Windows.Input;

[SupportedOSPlatform("windows")]
public sealed class WindowsInputInjectorTests
{
    private const uint ExtendedHookFlag = 0x01;
    private const uint AltDownHookFlag = 0x20;
    private static readonly VirtualDesktopBounds StandardDesktop = new(0, 0, 1920, 1080);

    [Fact]
    public void ScanCodeKeyDownUsesScanCodeAndMarker()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(123, InputEventType.KeyboardKeyDown, 65, 30, 0));

        var input = Assert.Single(native.LastInputs);
        Assert.Equal(NativeInputType.Keyboard, input.Type);
        Assert.Equal((ushort)0, input.Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)30, input.Data.Keyboard.ScanCode);
        Assert.Equal(NativeKeyboardInputFlags.ScanCode, input.Data.Keyboard.Flags);
        Assert.Equal(WindowsInputInjector.InjectionMarker, input.Data.Keyboard.ExtraInfo);
    }

    [Fact]
    public void ScanCodeKeyUpAddsKeyUpFlag()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyUp, 65, 30, 0));

        Assert.Equal(
            NativeKeyboardInputFlags.ScanCode | NativeKeyboardInputFlags.KeyUp,
            Assert.Single(native.LastInputs).Data.Keyboard.Flags);
    }

    [Theory]
    [InlineData(0u, 0x0008u)]
    [InlineData(ExtendedHookFlag, 0x0009u)]
    public void ExtendedFlagIsMappedOnlyWhenPresent(uint sourceFlags, uint expected)
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, sourceFlags));

        Assert.Equal((NativeKeyboardInputFlags)expected, Assert.Single(native.LastInputs).Data.Keyboard.Flags);
    }

    [Fact]
    public void VirtualKeyFallbackUsesVirtualKeyWithoutScanCodeFlag()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 0x41, 0, 0));

        var keyboard = Assert.Single(native.LastInputs).Data.Keyboard;
        Assert.Equal((ushort)0x41, keyboard.VirtualKey);
        Assert.Equal((ushort)0, keyboard.ScanCode);
        Assert.Equal(NativeKeyboardInputFlags.None, keyboard.Flags);
    }

    [Fact]
    public void VirtualKeyFallbackKeyUpUsesEventType()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyUp, 0x41, 0, 0));

        Assert.Equal(NativeKeyboardInputFlags.KeyUp, Assert.Single(native.LastInputs).Data.Keyboard.Flags);
    }

    [Theory]
    [InlineData(65536u, 0u, "VirtualKey")]
    [InlineData(1u, 65536u, "ScanCode")]
    public void KeyboardValuesLargerThanUShortAreRejected(uint virtualKey, uint scanCode, string parameterName)
    {
        var (injector, native, _) = CreateInjector();
        var input = new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, virtualKey, scanCode, 0);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => injector.Inject(input));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Empty(native.LastInputs);
    }

    [Fact]
    public void RawKeyboardFlagsAreNotCopiedIntoNativeFlags()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, uint.MaxValue & ~ExtendedHookFlag));

        Assert.Equal(NativeKeyboardInputFlags.ScanCode, Assert.Single(native.LastInputs).Data.Keyboard.Flags);
    }

    [Fact]
    public void AltDownHookFlagDoesNotInventAKeyboardInjectionFlag()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, AltDownHookFlag));

        Assert.Equal(NativeKeyboardInputFlags.ScanCode, Assert.Single(native.LastInputs).Data.Keyboard.Flags);
    }

    [Fact]
    public void TimestampDoesNotChangeKeyboardMapping()
    {
        var first = InjectKeyboardAt(0);
        var second = InjectKeyboardAt(18_000_000_000);

        Assert.Equal(first.Type, second.Type);
        Assert.Equal(first.Data.Keyboard.VirtualKey, second.Data.Keyboard.VirtualKey);
        Assert.Equal(first.Data.Keyboard.ScanCode, second.Data.Keyboard.ScanCode);
        Assert.Equal(first.Data.Keyboard.Flags, second.Data.Keyboard.Flags);
        Assert.Equal(first.Data.Keyboard.Time, second.Data.Keyboard.Time);
    }

    [Fact]
    public void MouseMoveUsesAbsoluteVirtualDesktopFlags()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(Move(500, 300));

        var mouse = Assert.Single(native.LastInputs).Data.Mouse;
        Assert.Equal(
            NativeMouseInputFlags.Move | NativeMouseInputFlags.Absolute | NativeMouseInputFlags.VirtualDesktop,
            mouse.Flags);
        Assert.Equal(WindowsInputInjector.InjectionMarker, mouse.ExtraInfo);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1919, 1079, 65535, 65535)]
    [InlineData(960, 540, 32785, 32798)]
    public void MouseCoordinatesAreNormalizedAcrossStandardDesktop(
        int x,
        int y,
        int expectedX,
        int expectedY)
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(Move(x, y));

        var mouse = Assert.Single(native.LastInputs).Data.Mouse;
        Assert.Equal(expectedX, mouse.X);
        Assert.Equal(expectedY, mouse.Y);
    }

    [Theory]
    [InlineData(-1920, 0, 0, 0)]
    [InlineData(-1, 1079, 65535, 65535)]
    [InlineData(-960, 540, 32785, 32798)]
    public void NegativeVirtualDesktopOriginIsNormalized(
        int x,
        int y,
        int expectedX,
        int expectedY)
    {
        var (injector, native, _) = CreateInjector(new VirtualDesktopBounds(-1920, 0, 1920, 1080));

        injector.Inject(Move(x, y));

        var mouse = Assert.Single(native.LastInputs).Data.Mouse;
        Assert.Equal(expectedX, mouse.X);
        Assert.Equal(expectedY, mouse.Y);
    }

    [Fact]
    public void NegativeYIsSupportedInsideVirtualDesktop()
    {
        var (injector, native, _) = CreateInjector(new VirtualDesktopBounds(0, -1080, 1920, 2160));

        injector.Inject(Move(0, -500));

        var mouse = Assert.Single(native.LastInputs).Data.Mouse;
        Assert.Equal(0, mouse.X);
        Assert.Equal(17606, mouse.Y);
    }

    [Fact]
    public void LargeVirtualDesktopBoundaryMapsWithoutOverflow()
    {
        var bounds = new VirtualDesktopBounds(-1_000_000_000, -1_000_000_000, 2_000_000_000, 2_000_000_000);
        var (injector, native, _) = CreateInjector(bounds);

        injector.Inject(Move(999_999_999, 999_999_999));

        var mouse = Assert.Single(native.LastInputs).Data.Mouse;
        Assert.Equal(65535, mouse.X);
        Assert.Equal(65535, mouse.Y);
    }

    [Theory]
    [InlineData(-1, 500)]
    [InlineData(1920, 500)]
    [InlineData(500, -1)]
    [InlineData(500, 1080)]
    public void CoordinatesOutsideVirtualDesktopAreRejected(int x, int y)
    {
        var (injector, native, _) = CreateInjector();

        Assert.Throws<ArgumentOutOfRangeException>(() => injector.Inject(Move(x, y)));
        Assert.Empty(native.LastInputs);
    }

    [Theory]
    [InlineData(10, 20, 1, 100, 10, 70, 0, 33098)]
    [InlineData(10, 20, 100, 1, 60, 20, 33098, 0)]
    public void OnePixelAxisNormalizesToZero(
        int left,
        int top,
        int width,
        int height,
        int x,
        int y,
        int expectedX,
        int expectedY)
    {
        var (injector, native, _) = CreateInjector(new VirtualDesktopBounds(left, top, width, height));

        injector.Inject(Move(x, y));

        var mouse = Assert.Single(native.LastInputs).Data.Mouse;
        Assert.Equal(expectedX, mouse.X);
        Assert.Equal(expectedY, mouse.Y);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(-1, 1080)]
    [InlineData(1920, 0)]
    [InlineData(1920, -1)]
    public void NonPositiveDesktopDimensionsAreRejected(int width, int height)
    {
        var (injector, native, _) = CreateInjector(new VirtualDesktopBounds(0, 0, width, height));

        Assert.Throws<InvalidOperationException>(() => injector.Inject(Move(0, 0)));
        Assert.Empty(native.LastInputs);
    }

    [Theory]
    [InlineData(MouseButton.Left, true, 0x0002u, 0u)]
    [InlineData(MouseButton.Left, false, 0x0004u, 0u)]
    [InlineData(MouseButton.Right, true, 0x0008u, 0u)]
    [InlineData(MouseButton.Right, false, 0x0010u, 0u)]
    [InlineData(MouseButton.Middle, true, 0x0020u, 0u)]
    [InlineData(MouseButton.Middle, false, 0x0040u, 0u)]
    [InlineData(MouseButton.XButton1, true, 0x0080u, 1u)]
    [InlineData(MouseButton.XButton1, false, 0x0100u, 1u)]
    [InlineData(MouseButton.XButton2, true, 0x0080u, 2u)]
    [InlineData(MouseButton.XButton2, false, 0x0100u, 2u)]
    public void MouseButtonsMapToTwoInputBatch(
        MouseButton button,
        bool isDown,
        uint expectedFlag,
        uint expectedData)
    {
        var (injector, native, _) = CreateInjector();
        var eventType = isDown ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp;

        injector.Inject(new MouseInputEvent(0, eventType, 100, 200, button, 0));

        Assert.Equal(2, native.LastInputs.Length);
        Assert.Equal(3415, native.LastInputs[0].Data.Mouse.X);
        Assert.Equal(12147, native.LastInputs[0].Data.Mouse.Y);
        Assert.Equal((NativeMouseInputFlags)expectedFlag, native.LastInputs[1].Data.Mouse.Flags);
        Assert.Equal(expectedData, native.LastInputs[1].Data.Mouse.MouseData);
        Assert.All(native.LastInputs, input => Assert.Equal(WindowsInputInjector.InjectionMarker, input.Data.Mouse.ExtraInfo));
    }

    [Theory]
    [InlineData(InputEventType.MouseButtonDown)]
    [InlineData(InputEventType.MouseButtonUp)]
    public void MouseButtonNoneIsRejected(InputEventType eventType)
    {
        var (injector, native, _) = CreateInjector();

        Assert.Throws<ArgumentException>(() => injector.Inject(
            new MouseInputEvent(0, eventType, 100, 200, MouseButton.None, 0)));
        Assert.Empty(native.LastInputs);
    }

    [Theory]
    [InlineData(InputEventType.MouseVerticalWheel, 120, 0x0800u, 120u)]
    [InlineData(InputEventType.MouseVerticalWheel, -120, 0x0800u, 4294967176u)]
    [InlineData(InputEventType.MouseVerticalWheel, 60, 0x0800u, 60u)]
    [InlineData(InputEventType.MouseVerticalWheel, -60, 0x0800u, 4294967236u)]
    [InlineData(InputEventType.MouseHorizontalWheel, 120, 0x1000u, 120u)]
    [InlineData(InputEventType.MouseHorizontalWheel, -120, 0x1000u, 4294967176u)]
    [InlineData(InputEventType.MouseHorizontalWheel, 17, 0x1000u, 17u)]
    public void MouseWheelPreservesSignedDeltaBits(
        InputEventType eventType,
        int delta,
        uint expectedFlag,
        uint expectedData)
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new MouseInputEvent(0, eventType, 100, 200, MouseButton.None, delta));

        Assert.Equal(2, native.LastInputs.Length);
        Assert.Equal(3415, native.LastInputs[0].Data.Mouse.X);
        Assert.Equal(12147, native.LastInputs[0].Data.Mouse.Y);
        Assert.Equal((NativeMouseInputFlags)expectedFlag, native.LastInputs[1].Data.Mouse.Flags);
        Assert.Equal(expectedData, native.LastInputs[1].Data.Mouse.MouseData);
        Assert.Equal(WindowsInputInjector.InjectionMarker, native.LastInputs[1].Data.Mouse.ExtraInfo);
    }

    [Theory]
    [InlineData(InputEventType.MouseVerticalWheel)]
    [InlineData(InputEventType.MouseHorizontalWheel)]
    public void ZeroWheelDeltaIsRejected(InputEventType eventType)
    {
        var (injector, native, _) = CreateInjector();

        Assert.Throws<ArgumentException>(() => injector.Inject(
            new MouseInputEvent(0, eventType, 100, 200, MouseButton.None, 0)));
        Assert.Empty(native.LastInputs);
    }

    [Theory]
    [MemberData(nameof(InvalidMouseSemanticEvents))]
    public void InvalidMouseSemanticCombinationsAreRejected(MouseInputEvent inputEvent)
    {
        var (injector, native, _) = CreateInjector();

        Assert.Throws<ArgumentException>(() => injector.Inject(inputEvent));
        Assert.Empty(native.LastInputs);
    }

    public static TheoryData<MouseInputEvent> InvalidMouseSemanticEvents => new()
    {
        Move(10, 10, MouseButton.Left, 0),
        Move(10, 10, MouseButton.None, 120),
        new MouseInputEvent(0, InputEventType.MouseButtonDown, 10, 10, MouseButton.Left, 120),
        new MouseInputEvent(0, InputEventType.MouseVerticalWheel, 10, 10, MouseButton.Left, 120),
        new MouseInputEvent(0, InputEventType.MouseButtonUp, 10, 10, (MouseButton)99, 0),
    };

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(2u, 2u)]
    public void CompleteSendInputBatchSucceeds(uint injectedCount, uint expectedRequestedCount)
    {
        var (injector, native, _) = CreateInjector();
        native.Result = new NativeInjectionResult(injectedCount, 0);
        InputEvent input = injectedCount == 1
            ? new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0)
            : new MouseInputEvent(0, InputEventType.MouseButtonDown, 10, 10, MouseButton.Left, 0);

        injector.Inject(input);

        Assert.Equal(expectedRequestedCount, (uint)native.LastInputs.Length);
        Assert.Equal(Marshal.SizeOf<NativeInput>(), native.LastInputSize);
        Assert.Equal(1, native.CallCount);
    }

    [Theory]
    [InlineData(0u, 1u, 5)]
    [InlineData(0u, 2u, 5)]
    [InlineData(1u, 2u, 0)]
    public void IncompleteSendInputBatchThrowsWithCountsAndError(
        uint injectedCount,
        uint requestedCount,
        int errorCode)
    {
        var (injector, native, _) = CreateInjector();
        native.Result = new NativeInjectionResult(injectedCount, errorCode);
        InputEvent input = requestedCount == 1
            ? new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0)
            : new MouseInputEvent(0, InputEventType.MouseButtonDown, 10, 10, MouseButton.Left, 0);

        var exception = Assert.Throws<InputInjectionException>(() => injector.Inject(input));

        Assert.Equal(requestedCount, exception.RequestedInputCount);
        Assert.Equal(injectedCount, exception.InjectedInputCount);
        Assert.Equal(errorCode, exception.Win32ErrorCode);
    }

    [Fact]
    public void GenericInterfaceRoutesKeyboardAndMouseEvents()
    {
        var (injector, native, desktop) = CreateInjector();

        ((IInputInjector)injector).Inject(new KeyboardInputEvent(0, InputEventType.KeyboardKeyDown, 65, 30, 0));
        Assert.Equal(NativeInputType.Keyboard, Assert.Single(native.LastInputs).Type);
        ((IInputInjector)injector).Inject(Move(10, 10));
        Assert.Equal(NativeInputType.Mouse, Assert.Single(native.LastInputs).Type);
        Assert.Equal(1, desktop.CallCount);
    }

    [Fact]
    public void UnknownInputEventSubtypeIsRejected()
    {
        var (injector, native, _) = CreateInjector();

        Assert.Throws<NotSupportedException>(() => injector.Inject(new UnknownInputEvent()));
        Assert.Empty(native.LastInputs);
    }

    [Fact]
    public void NullInputEventIsRejected()
    {
        var (injector, native, _) = CreateInjector();

        Assert.Throws<ArgumentNullException>(() => injector.Inject(null!));
        Assert.Empty(native.LastInputs);
    }

    [Fact]
    public void InjectionDoesNotMutateDomainEvent()
    {
        var input = new MouseInputEvent(999, InputEventType.MouseButtonDown, -500, 200, MouseButton.XButton2, 0);
        var before = input with { };
        var negativeDesktopInjector = CreateInjector(new VirtualDesktopBounds(-1920, 0, 3840, 1080)).Injector;

        negativeDesktopInjector.Inject(input);

        Assert.Equal(before, input);
    }

    [Fact]
    public void EveryMouseInputInBatchCarriesMarker()
    {
        var (injector, native, _) = CreateInjector();

        injector.Inject(new MouseInputEvent(0, InputEventType.MouseHorizontalWheel, 10, 10, MouseButton.None, -120));

        Assert.All(native.LastInputs, input => Assert.Equal(WindowsInputInjector.InjectionMarker, input.Data.Mouse.ExtraInfo));
    }

    private static NativeInput InjectKeyboardAt(long timestampUs)
    {
        var (injector, native, _) = CreateInjector();
        injector.Inject(new KeyboardInputEvent(timestampUs, InputEventType.KeyboardKeyDown, 65, 30, 0));
        return Assert.Single(native.LastInputs);
    }

    private static MouseInputEvent Move(
        int x,
        int y,
        MouseButton button = MouseButton.None,
        int wheelDelta = 0) =>
        new(0, InputEventType.MouseMove, x, y, button, wheelDelta);

    private static InjectorContext CreateInjector(VirtualDesktopBounds? bounds = null)
    {
        var native = new FakeWindowsInputNativeApi();
        var desktop = new FakeVirtualDesktopProvider(bounds ?? StandardDesktop);
        return new InjectorContext(new WindowsInputInjector(native, desktop), native, desktop);
    }

    private sealed record InjectorContext(
        WindowsInputInjector Injector,
        FakeWindowsInputNativeApi Native,
        FakeVirtualDesktopProvider Desktop);

    private sealed class FakeWindowsInputNativeApi : IWindowsInputNativeApi
    {
        public NativeInjectionResult? Result { get; set; }

        public NativeInput[] LastInputs { get; private set; } = [];

        public int LastInputSize { get; private set; }

        public int CallCount { get; private set; }

        public NativeInjectionResult Send(NativeInput[] inputs, int inputSize)
        {
            LastInputs = [.. inputs];
            LastInputSize = inputSize;
            CallCount++;
            return Result ?? new NativeInjectionResult((uint)inputs.Length, 0);
        }
    }

    private sealed class FakeVirtualDesktopProvider(VirtualDesktopBounds bounds) : IVirtualDesktopProvider
    {
        public int CallCount { get; private set; }

        public VirtualDesktopBounds GetBounds()
        {
            CallCount++;
            return bounds;
        }
    }

    private sealed record UnknownInputEvent() : InputEvent(0, InputEventType.KeyboardKeyDown);
}
