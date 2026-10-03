namespace MacroRecorder.Infrastructure.Storage;

/// <summary>Portable local data paths, independent of the process working directory.</summary>
public sealed class ApplicationPaths
{
    public ApplicationPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("The data directory must be an absolute path.", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
        MacrosDirectory = Path.Combine(RootDirectory, "macros");
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }
    public string MacrosDirectory { get; }
    public string ConfigDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");

    public static ApplicationPaths CreateDefault() => new(Path.Combine(AppContext.BaseDirectory, "data"));

    /// <summary>Creates missing directories without replacing existing contents. IO failures propagate.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(MacrosDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
