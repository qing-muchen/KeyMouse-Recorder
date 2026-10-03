using System.Runtime.Versioning;
using MacroRecorder.Core.Recording.Keyboard;
using MacroRecorder.Infrastructure.Windows.Keyboard;

namespace MacroRecorder.Core.Tests.Windows.Keyboard;

[SupportedOSPlatform("windows")]
public sealed class KeyboardHookServiceTests
{
    [Fact]
    public void StartStopAndRestartOwnsAndReleasesTheHookThread()
    {
        using var service = new KeyboardHookService();

        service.StartCapture();
        Assert.True(service.IsRunning);
        service.StopCapture();
        Assert.False(service.IsRunning);

        service.StartCapture();
        Assert.True(service.IsRunning);
        service.StopCapture();
        service.StopCapture();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void StartingTwiceIsRejected()
    {
        using var service = new KeyboardHookService();
        service.StartCapture();

        Assert.Throws<InvalidOperationException>(service.StartCapture);
    }

    [Fact]
    public void DisposeStopsCaptureIsIdempotentAndPreventsRestart()
    {
        var service = new KeyboardHookService();
        service.StartCapture();

        service.Dispose();
        service.Dispose();

        Assert.False(service.IsRunning);
        Assert.Throws<ObjectDisposedException>(service.StartCapture);
    }

    [Fact]
    public void SubscriberExceptionIsContainedAndObservable()
    {
        using var service = new KeyboardHookService();
        service.EventReceived += _ => throw new InvalidOperationException("callback failed");

        var exception = Record.Exception(() => service.PublishSafely(
            new KeyboardCaptureEvent(KeyboardTransition.KeyDown, 65, 30, 0, false)));

        Assert.Null(exception);
        Assert.IsType<InvalidOperationException>(service.LastCallbackException);
        Assert.Equal("callback failed", service.LastCallbackException?.Message);
    }
}
