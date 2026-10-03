namespace MacroRecorder.App.ViewModels;

/// <summary>Read-only shell. Interactive application state and commands are introduced in later phases.</summary>
public sealed class MainWindowViewModel(string dataDirectory)
{
    public string Status { get; } = "Idle · 未录制";
    public string PhaseDescription { get; } = "项目基础已就绪。录制、播放和宏文件管理将在后续阶段逐步实现。";
    public string DataDirectory { get; } = dataDirectory;
}
