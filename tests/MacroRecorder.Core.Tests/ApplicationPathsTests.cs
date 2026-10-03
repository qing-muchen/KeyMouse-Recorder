using MacroRecorder.Infrastructure.Storage;

namespace MacroRecorder.Core.Tests;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void EnsureCreatedCreatesSeparateDirectoriesAndPreservesExistingFiles()
    {
        using var directory = new TestDirectory();
        var paths = new ApplicationPaths(directory.Path);
        paths.EnsureCreated();
        var existingFile = Path.Combine(paths.MacrosDirectory, "existing.json");
        File.WriteAllText(existingFile, "existing content");

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.MacrosDirectory));
        Assert.True(Directory.Exists(paths.ConfigDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.Equal("existing content", File.ReadAllText(existingFile));
        Assert.Equal(Path.Combine(paths.ConfigDirectory, "settings.json"), paths.SettingsFilePath);
        Assert.False(File.Exists(paths.SettingsFilePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-data")]
    public void RejectsAmbiguousRootPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new ApplicationPaths(path));
    }

    [Fact]
    public void EnsureCreatedReportsFileSystemFailure()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "macros"), "blocking file");
        var paths = new ApplicationPaths(directory.Path);

        Assert.Throws<IOException>(paths.EnsureCreated);
    }

    [Fact]
    public void DefaultDirectoryIsRelativeToApplicationRatherThanWorkingDirectory()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data"), ApplicationPaths.CreateDefault().RootDirectory);
    }
}
