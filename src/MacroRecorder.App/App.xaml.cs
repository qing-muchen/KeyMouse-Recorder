using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MacroRecorder.App.ViewModels;
using MacroRecorder.Core.Diagnostics;
using MacroRecorder.Infrastructure.Diagnostics;
using MacroRecorder.Infrastructure.Storage;

namespace MacroRecorder.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; OnExit disposes the logger in a finally block.")]
public partial class App : Application
{
    private LocalFileLogger? logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var paths = ApplicationPaths.CreateDefault();
            paths.EnsureCreated();
            logger = new LocalFileLogger(paths.LogsDirectory);
            logger.Write(AppLogLevel.Information, AppLogEvent.ApplicationStarted);

            MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(paths.RootDirectory),
            };
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            ReportFailure(AppLogEvent.StartupFailed, exception);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        try
        {
            logger?.Write(AppLogLevel.Information, AppLogEvent.ApplicationStopped);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowLogFailure();
        }
        finally
        {
            try
            {
                logger?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ShowLogFailure();
            }

            base.OnExit(e);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFailure(AppLogEvent.UnhandledException, e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void ReportFailure(AppLogEvent eventId, Exception exception)
    {
        try
        {
            logger?.Write(AppLogLevel.Error, eventId, exception);
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            ShowLogFailure();
        }

        MessageBox.Show(
            $"应用无法继续运行（{eventId} / {exception.GetType().Name}）。请检查程序目录写入权限及本地日志。",
            "Macro Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void ShowLogFailure() => MessageBox.Show(
        "本地日志写入或关闭失败，请检查程序目录写入权限和可用磁盘空间。",
        "Macro Recorder", MessageBoxButton.OK, MessageBoxImage.Warning);
}
