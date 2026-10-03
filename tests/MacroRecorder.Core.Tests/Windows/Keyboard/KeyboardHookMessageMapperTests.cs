using MacroRecorder.Core.Recording.Keyboard;
using MacroRecorder.Infrastructure.Windows.Keyboard;

namespace MacroRecorder.Core.Tests.Windows.Keyboard;

public sealed class KeyboardHookMessageMapperTests
{
    [Theory]
    [InlineData(NativeKeyboardMethods.WmKeyDown, KeyboardTransition.KeyDown)]
    [InlineData(NativeKeyboardMethods.WmSystemKeyDown, KeyboardTransition.KeyDown)]
    [InlineData(NativeKeyboardMethods.WmKeyUp, KeyboardTransition.KeyUp)]
    [InlineData(NativeKeyboardMethods.WmSystemKeyUp, KeyboardTransition.KeyUp)]
    public void MapsSupportedWindowsMessages(uint message, KeyboardTransition expected)
    {
        Assert.True(KeyboardHookMessageMapper.TryMap(0, (nint)message, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(NativeKeyboardMethods.WmQuit)]
    [InlineData(uint.MaxValue)]
    public void UnknownMessageIsIgnored(uint message)
    {
        Assert.False(KeyboardHookMessageMapper.TryMap(0, (nint)message, out _));
    }

    [Fact]
    public void NegativeHookCodeIsIgnoredBeforeMessageMapping()
    {
        Assert.False(KeyboardHookMessageMapper.TryMap(
            -1,
            (nint)NativeKeyboardMethods.WmKeyDown,
            out _));
    }

    [Theory]
    [InlineData(0U, false)]
    [InlineData((uint)LowLevelKeyboardFlags.Extended, false)]
    [InlineData((uint)LowLevelKeyboardFlags.AltDown, false)]
    [InlineData((uint)LowLevelKeyboardFlags.Injected, true)]
    [InlineData((uint)LowLevelKeyboardFlags.LowerIntegrityInjected, true)]
    [InlineData((uint)(LowLevelKeyboardFlags.Injected | LowLevelKeyboardFlags.Extended), true)]
    [InlineData((uint)(LowLevelKeyboardFlags.LowerIntegrityInjected | LowLevelKeyboardFlags.AltDown), true)]
    public void ClassifiesInjectedFlagsWithoutRejectingOrdinaryFlags(uint value, bool expected)
    {
        Assert.Equal(expected, KeyboardHookMessageMapper.IsInjected((LowLevelKeyboardFlags)value));
    }
}
