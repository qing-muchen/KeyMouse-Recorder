using System.Runtime.Versioning;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Recording.Mouse;
using MacroRecorder.Infrastructure.Windows.Mouse;

namespace MacroRecorder.Core.Tests.Windows.Mouse;

[SupportedOSPlatform("windows")]
public sealed class MouseHookServiceTests
{
    [Fact]
    public void StartStopAndRestartOwnsAndReleasesHookThread()
    {
        using var service = new MouseHookService();

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
        using var service = new MouseHookService();
        service.StartCapture();

        Assert.Throws<InvalidOperationException>(service.StartCapture);
    }

    [Fact]
    public void DisposeStopsCaptureIsIdempotentAndPreventsRestart()
    {
        var service = new MouseHookService();
        service.StartCapture();

        service.Dispose();
        service.Dispose();

        Assert.False(service.IsRunning);
        Assert.Throws<ObjectDisposedException>(service.StartCapture);
    }

    [Fact]
    public void SubscriberExceptionIsContainedAndObservable()
    {
        using var service = new MouseHookService();
        service.EventReceived += _ => throw new InvalidOperationException("callback failed");

        var exception = Record.Exception(() => service.PublishSafely(
            new MouseCaptureEvent(MouseCaptureKind.Move, 1, 2, MouseButton.None, 0, false)));

        Assert.Null(exception);
        Assert.IsType<InvalidOperationException>(service.LastCallbackException);
        Assert.Equal("callback failed", service.LastCallbackException?.Message);
    }
}
