using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MacroRecorder.Core.Models;
using MacroRecorder.Core.Models.Input;
using MacroRecorder.Core.Tests.Models;
using MacroRecorder.Core.Validation;
using MacroRecorder.Infrastructure.Library;
using MacroRecorder.Infrastructure.Persistence;

namespace MacroRecorder.Core.Tests.Library;

public sealed class MacroRepositoryTests
{
    private readonly MacroFileStore fileStore = new();

    [Fact]
    public async Task MissingRootIsCreatedAndReturnsEmptySnapshot()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "missing-library");
        var repository = new MacroRepository(root);

        var entries = await repository.ListAsync();

        Assert.True(Directory.Exists(root));
        Assert.Empty(entries);
        Assert.True(entries.IsEmpty);
    }

    [Fact]
    public async Task OneValidMacroProducesCompleteSummaryWithoutEvents()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        var macro = DomainTestData.CreateMacro();
        await repository.SaveAsync("valid", macro);

        var entry = Assert.Single(await repository.ListAsync());

        Assert.Equal("valid.json", entry.FileName);
        Assert.Equal(Path.Combine(root, "valid.json"), entry.FullPath);
        Assert.Equal(MacroLibraryEntryStatus.Valid, entry.Status);
        Assert.NotNull(entry.Summary);
        Assert.Equal(macro.Id, entry.Summary.Id);
        Assert.Equal(macro.Name, entry.Summary.Name);
        Assert.Equal(macro.Description, entry.Summary.Description);
        Assert.Equal(macro.Recording.DurationUs, entry.Summary.DurationUs);
        Assert.Equal(macro.Recording.EventCount, entry.Summary.EventCount);
        Assert.Equal(macro.CreatedAt, entry.Summary.CreatedAt);
        Assert.Equal(macro.UpdatedAt, entry.Summary.UpdatedAt);
        Assert.True(entry.Validation!.IsValid);
        Assert.Null(entry.Error);
        Assert.DoesNotContain(
            typeof(MacroLibraryEntry).GetProperties(),
            static property => property.PropertyType == typeof(Macro)
                || property.Name.Contains("Events", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(MacroLibrarySummary).GetProperties(),
            static property => property.PropertyType == typeof(Macro)
                || property.Name.Contains("Events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EntriesAreDeterministicallyOrderedByFilename()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        await repository.SaveAsync("zeta", DomainTestData.CreateMacro() with { Name = "First by metadata" });
        await repository.SaveAsync("Alpha", DomainTestData.CreateMacro() with { Name = "Last by metadata" });
        await repository.SaveAsync("beta", DomainTestData.CreateMacro());

        var first = await repository.ListAsync();
        var second = await repository.ListAsync();

        Assert.Equal(["Alpha.json", "beta.json", "zeta.json"], first.Select(static entry => entry.FileName));
        Assert.Equal(
            first.Select(static entry => entry.FileName),
            second.Select(static entry => entry.FileName));
    }

    [Fact]
    public async Task UppercaseJsonExtensionIsIncluded()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await fileStore.SaveAsync(Path.Combine(root, "UPPER.JSON"), DomainTestData.CreateMacro());

        var entry = Assert.Single(await repository.ListAsync());

        Assert.Equal("UPPER.JSON", entry.FileName);
        Assert.Equal(MacroLibraryEntryStatus.Valid, entry.Status);
    }

    [Fact]
    public async Task NonJsonTempAndNestedFilesAreIgnoredWithoutDeletion()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        var textPath = Path.Combine(root, "README.txt");
        var tempPath = Path.Combine(root, ".daily.json.abc.tmp");
        var nestedDirectory = Path.Combine(root, "archive");
        var nestedPath = Path.Combine(nestedDirectory, "nested.json");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(textPath, "keep");
        await File.WriteAllTextAsync(tempPath, "keep");
        await fileStore.SaveAsync(nestedPath, DomainTestData.CreateMacro());
        await repository.SaveAsync("visible", DomainTestData.CreateMacro());

        var entries = await repository.ListAsync();

        Assert.Equal("visible.json", Assert.Single(entries).FileName);
        Assert.True(File.Exists(textPath));
        Assert.True(File.Exists(tempPath));
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public async Task ValidInvalidAndBrokenFilesAreIsolatedInOneSnapshot()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        await repository.SaveAsync("good-a", DomainTestData.CreateMacro() with { Name = "Good A" });
        await fileStore.SaveAsync(
            Path.Combine(root, "invalid.json"),
            DomainTestData.CreateMacro() with { Id = Guid.Empty });
        await File.WriteAllTextAsync(Path.Combine(root, "broken.json"), "{ broken json", Encoding.UTF8);
        await repository.SaveAsync("good-b", DomainTestData.CreateMacro() with { Name = "Good B" });

        var entries = await repository.ListAsync();

        Assert.Equal(4, entries.Length);
        Assert.Equal(
            ["broken.json", "good-a.json", "good-b.json", "invalid.json"],
            entries.Select(static entry => entry.FileName));
        Assert.Equal(MacroLibraryEntryStatus.Unreadable, entries[0].Status);
        Assert.Equal(MacroLibraryEntryStatus.Valid, entries[1].Status);
        Assert.Equal(MacroLibraryEntryStatus.Valid, entries[2].Status);
        Assert.Equal(MacroLibraryEntryStatus.Invalid, entries[3].Status);
        Assert.Null(entries[0].Summary);
        Assert.Null(entries[0].Validation);
        Assert.NotNull(entries[0].Error);
        Assert.Contains(
            entries[3].Validation!.Issues,
            static issue => issue.Code == ValidationCodes.MacroIdEmpty);
    }

    [Fact]
    public async Task FutureSchemaIsInvalidRatherThanUnreadable()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        await fileStore.SaveAsync(
            Path.Combine(root, "future.json"),
            DomainTestData.CreateMacro() with { SchemaVersion = 999 });

        var entry = Assert.Single(await repository.ListAsync());

        Assert.Equal(MacroLibraryEntryStatus.Invalid, entry.Status);
        Assert.NotNull(entry.Summary);
        Assert.Contains(
            entry.Validation!.Issues,
            static issue => issue.Code == ValidationCodes.SchemaVersionUnsupported);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task WarningOnlyMacroIsValidAndRetainsWarnings()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        await repository.SaveAsync("empty", CreateEmptyMacro());

        var entry = Assert.Single(await repository.ListAsync());

        Assert.Equal(MacroLibraryEntryStatus.Valid, entry.Status);
        Assert.True(entry.Validation!.IsValid);
        Assert.Contains(
            entry.Validation.Issues,
            static issue => issue.Code == ValidationCodes.EmptyMacro
                && issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task DuplicateMacroNamesAndIdsAreAllowedAcrossDifferentFiles()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var macro = DomainTestData.CreateMacro();
        await repository.SaveAsync("first", macro);
        await repository.SaveAsync("second", macro);

        var entries = await repository.ListAsync();

        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry => Assert.Equal(macro.Id, entry.Summary!.Id));
        Assert.All(entries, entry => Assert.Equal(macro.Name, entry.Summary!.Name));
    }

    [Fact]
    public async Task RepeatedListReflectsCurrentDiskWithoutCache()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        Assert.Empty(await repository.ListAsync());
        await repository.SaveAsync("added", DomainTestData.CreateMacro());
        Assert.Single(await repository.ListAsync());

        await repository.DeleteAsync("added");

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task ListingDoesNotModifyReadableOrBrokenFiles()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("valid", DomainTestData.CreateMacro());
        var brokenPath = Path.Combine(root, "broken.json");
        await File.WriteAllTextAsync(brokenPath, "{ incomplete", Encoding.UTF8);
        var validPath = Path.Combine(root, "valid.json");
        var validBefore = await File.ReadAllBytesAsync(validPath);
        var brokenBefore = await File.ReadAllBytesAsync(brokenPath);

        _ = await repository.ListAsync();

        Assert.Equal(validBefore, await File.ReadAllBytesAsync(validPath));
        Assert.Equal(brokenBefore, await File.ReadAllBytesAsync(brokenPath));
    }

    [Fact]
    public async Task UnreadableErrorContainsTypeAndMessageButNoFileContentOrStackTrace()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "broken.json"), "{ private-content-marker", Encoding.UTF8);

        var entry = Assert.Single(await repository.ListAsync());

        Assert.Equal(nameof(JsonException), entry.Error!.ErrorType);
        Assert.False(string.IsNullOrWhiteSpace(entry.Error.Message));
        Assert.DoesNotContain("private-content-marker", entry.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", entry.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneHundredMacroLibraryListsEveryEntryInStableOrder()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        for (var index = 99; index >= 0; index--)
        {
            await repository.SaveAsync($"macro-{index:D3}", DomainTestData.CreateMacro() with
            {
                Id = Guid.NewGuid(),
                Name = $"Generated {index:D3}",
            });
        }

        var entries = await repository.ListAsync();

        Assert.Equal(100, entries.Length);
        Assert.Equal("macro-000.json", entries[0].FileName);
        Assert.Equal("macro-099.json", entries[^1].FileName);
        Assert.All(entries, static entry => Assert.Equal(MacroLibraryEntryStatus.Valid, entry.Status));
    }

    [Fact]
    public async Task PreCancelledListPropagatesWithoutCreatingRoot()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "cancelled-library");
        var repository = new MacroRepository(root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListAsync(cancellation.Token));

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task LoadValidMacroReturnsCompleteMacroAndValidation()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var original = DomainTestData.CreateMacro();
        await repository.SaveAsync("valid", original);

        var result = await repository.LoadAsync("valid");

        Assert.True(result.IsValid);
        Assert.True(result.Validation.IsValid);
        DomainTestData.AssertMacroEquivalent(original, result.Macro);
    }

    [Fact]
    public async Task LoadInvalidMacroReturnsMacroAndInvalidValidationWithoutRepair()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        var invalid = DomainTestData.CreateMacro() with { Id = Guid.Empty };
        await fileStore.SaveAsync(Path.Combine(root, "invalid.json"), invalid);

        var result = await repository.LoadAsync("invalid");

        Assert.False(result.IsValid);
        Assert.Equal(Guid.Empty, result.Macro.Id);
        Assert.Contains(result.Validation.Issues, static issue => issue.Code == ValidationCodes.MacroIdEmpty);
    }

    [Fact]
    public async Task LoadMalformedJsonPropagatesJsonException()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "broken.json"), "{", Encoding.UTF8);

        await Assert.ThrowsAsync<JsonException>(() => repository.LoadAsync("broken"));
    }

    [Fact]
    public async Task LoadMissingMacroPropagatesFileNotFound()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<FileNotFoundException>(() => repository.LoadAsync("missing"));
    }

    [Fact]
    public async Task LoadSupportsSpacesUnicodeAndExplicitExtension()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var macro = DomainTestData.CreateMacro() with { Name = "测试 宏" };
        await repository.SaveAsync("测试 macro.json", macro);

        var result = await repository.LoadAsync("测试 macro.json");

        DomainTestData.AssertMacroEquivalent(macro, result.Macro);
    }

    [Fact]
    public async Task RepeatedLoadDoesNotModifySourceBytes()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("repeat", DomainTestData.CreateMacro());
        var path = Path.Combine(root, "repeat.json");
        var before = await File.ReadAllBytesAsync(path);

        var first = await repository.LoadAsync("repeat");
        var second = await repository.LoadAsync("repeat");

        DomainTestData.AssertMacroEquivalent(first.Macro, second.Macro);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task PreCancelledLoadPropagates()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        await repository.SaveAsync("existing", DomainTestData.CreateMacro());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.LoadAsync("existing", cancellation.Token));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("..\\outside.json")]
    [InlineData("subdir/file.json")]
    [InlineData("subdir\\file.json")]
    [InlineData("not-json.txt")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task LoadRejectsUnsafeOrUnsupportedFilename(string fileName)
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.LoadAsync(fileName));
    }

    [Fact]
    public async Task LoadRejectsAbsolutePathOutsideRoot()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var outside = Path.Combine(directory.Path, "outside.json");

        await Assert.ThrowsAsync<ArgumentException>(() => repository.LoadAsync(outside));
    }

    [Fact]
    public async Task SaveValidMacroCreatesNormalizedJsonFile()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        var macro = DomainTestData.CreateMacro();

        var validation = await repository.SaveAsync("created", macro);

        Assert.True(validation.IsValid);
        Assert.True(File.Exists(Path.Combine(root, "created.json")));
        DomainTestData.AssertMacroEquivalent(macro, (await repository.LoadAsync("created.json")).Macro);
    }

    [Fact]
    public async Task SaveWarningOnlyMacroSucceedsAndReturnsWarning()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);

        var validation = await repository.SaveAsync("empty", CreateEmptyMacro());

        Assert.True(validation.IsValid);
        Assert.Contains(validation.Issues, static issue => issue.Code == ValidationCodes.EmptyMacro);
        Assert.True(File.Exists(Path.Combine(root, "empty.json")));
    }

    [Fact]
    public async Task InvalidSaveThrowsStructuredExceptionAndCreatesNoFile()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        var invalid = DomainTestData.CreateMacro() with { Name = " " };

        var exception = await Assert.ThrowsAsync<MacroValidationException>(() =>
            repository.SaveAsync("invalid", invalid));

        Assert.False(exception.ValidationResult.IsValid);
        Assert.Contains(
            exception.ValidationResult.Issues,
            static issue => issue.Code == ValidationCodes.MacroNameEmpty);
        Assert.DoesNotContain(ValidationCodes.MacroNameEmpty, exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "invalid.json")));
    }

    [Fact]
    public async Task InvalidOverwritePreservesExistingTargetBytes()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        var original = DomainTestData.CreateMacro() with { Name = "Original" };
        await repository.SaveAsync("protected", original);
        var path = Path.Combine(root, "protected.json");
        var before = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<MacroValidationException>(() =>
            repository.SaveAsync("protected", original with { Id = Guid.Empty }));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        DomainTestData.AssertMacroEquivalent(original, (await repository.LoadAsync("protected")).Macro);
    }

    [Fact]
    public async Task ValidSaveOverwritesThroughMacroFileStore()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var first = DomainTestData.CreateMacro() with { Name = "First" };
        var second = DomainTestData.CreateMacro() with { Name = "Second" };
        await repository.SaveAsync("overwrite", first);

        await repository.SaveAsync("overwrite", second);

        DomainTestData.AssertMacroEquivalent(second, (await repository.LoadAsync("overwrite")).Macro);
    }

    [Fact]
    public async Task SaveWithDifferentFilenameProvidesSaveAsSemantics()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var macro = DomainTestData.CreateMacro();
        await repository.SaveAsync("original", macro);

        await repository.SaveAsync("copy", macro);

        var entries = await repository.ListAsync();
        Assert.Equal(["copy.json", "original.json"], entries.Select(static entry => entry.FileName));
        DomainTestData.AssertMacroEquivalent(macro, (await repository.LoadAsync("copy")).Macro);
    }

    [Fact]
    public async Task SaveDoesNotMutateMacroOrEvents()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var macro = DomainTestData.CreateMacro();
        var snapshot = macro with { };
        var eventReferences = macro.Events.ToArray();

        await repository.SaveAsync("unchanged", macro);

        DomainTestData.AssertMacroEquivalent(snapshot, macro);
        Assert.Equal(eventReferences, macro.Events);
    }

    [Fact]
    public async Task PreCancelledSaveCreatesNoRootOrFile()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "cancelled-library");
        var repository = new MacroRepository(root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SaveAsync("cancelled", DomainTestData.CreateMacro(), cancellation.Token));

        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("..\\outside.json")]
    [InlineData("subdir/file.json")]
    [InlineData("subdir\\file.json")]
    [InlineData("not-json.txt")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".json")]
    [InlineData(" .json")]
    public async Task SaveRejectsUnsafeOrUnsupportedFilename(string fileName)
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.SaveAsync(fileName, DomainTestData.CreateMacro()));
    }

    [Fact]
    public async Task SaveRejectsAbsolutePathOutsideRoot()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var outside = Path.Combine(directory.Path, "outside.json");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.SaveAsync(outside, DomainTestData.CreateMacro()));
    }

    [Fact]
    public async Task RenameMovesFileWithoutChangingBytesOrMacroMetadata()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        var macro = DomainTestData.CreateMacro() with { Name = "Internal name" };
        await repository.SaveAsync("old", macro);
        var oldPath = Path.Combine(root, "old.json");
        var before = await File.ReadAllBytesAsync(oldPath);

        await repository.RenameAsync("old", "new");

        Assert.False(File.Exists(oldPath));
        var newPath = Path.Combine(root, "new.json");
        Assert.True(File.Exists(newPath));
        Assert.Equal(before, await File.ReadAllBytesAsync(newPath));
        var loaded = await repository.LoadAsync("new");
        Assert.Equal(macro.Name, loaded.Macro.Name);
        Assert.Equal(macro.UpdatedAt, loaded.Macro.UpdatedAt);
        Assert.Equal(macro.Id, loaded.Macro.Id);
    }

    [Fact]
    public async Task RenameMissingSourceThrowsFileNotFound()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<FileNotFoundException>(() => repository.RenameAsync("missing", "new"));
    }

    [Fact]
    public async Task RenameDoesNotOverwriteExistingTarget()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("source", DomainTestData.CreateMacro() with { Name = "Source" });
        await repository.SaveAsync("target", DomainTestData.CreateMacro() with { Name = "Target" });
        var sourceBefore = await File.ReadAllBytesAsync(Path.Combine(root, "source.json"));
        var targetBefore = await File.ReadAllBytesAsync(Path.Combine(root, "target.json"));

        await Assert.ThrowsAsync<IOException>(() => repository.RenameAsync("source", "target"));

        Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(Path.Combine(root, "source.json")));
        Assert.Equal(targetBefore, await File.ReadAllBytesAsync(Path.Combine(root, "target.json")));
    }

    [Fact]
    public async Task RenameUnreadableFileIsAllowed()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "broken.json"), "{", Encoding.UTF8);

        await repository.RenameAsync("broken", "renamed-broken");

        Assert.False(File.Exists(Path.Combine(root, "broken.json")));
        Assert.True(File.Exists(Path.Combine(root, "renamed-broken.json")));
    }

    [Fact]
    public async Task RenameSameFilenameIsSafeNoOp()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("same", DomainTestData.CreateMacro());
        var path = Path.Combine(root, "same.json");
        var before = await File.ReadAllBytesAsync(path);

        await repository.RenameAsync("same", "same.json");

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CaseOnlyRenameIsRejectedBeforeChangingFile()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("daily", DomainTestData.CreateMacro());
        var path = Path.Combine(root, "daily.json");
        var before = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<IOException>(() => repository.RenameAsync("daily", "Daily"));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("../outside.json", "new.json")]
    [InlineData("old.json", "../outside.json")]
    [InlineData("subdir/old.json", "new.json")]
    [InlineData("old.json", "subdir/new.json")]
    public async Task RenameRejectsTraversalAndNestedPaths(string oldFileName, string newFileName)
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.RenameAsync(oldFileName, newFileName));
    }

    [Fact]
    public async Task RenameRejectsAbsoluteSourceAndTargetOutsideRoot()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var outside = Path.Combine(directory.Path, "outside.json");

        await Assert.ThrowsAsync<ArgumentException>(() => repository.RenameAsync(outside, "new"));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.RenameAsync("old", outside));
    }

    [Fact]
    public async Task PreCancelledRenameDoesNotMoveFile()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("old", DomainTestData.CreateMacro());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.RenameAsync("old", "new", cancellation.Token));

        Assert.True(File.Exists(Path.Combine(root, "old.json")));
        Assert.False(File.Exists(Path.Combine(root, "new.json")));
    }

    [Fact]
    public async Task DeleteExistingMacroRemovesItFromDiskAndListing()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("delete-me", DomainTestData.CreateMacro());

        await repository.DeleteAsync("delete-me");

        Assert.False(File.Exists(Path.Combine(root, "delete-me.json")));
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task DeleteMissingMacroThrowsFileNotFound()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<FileNotFoundException>(() => repository.DeleteAsync("missing"));
    }

    [Fact]
    public async Task DeleteUnreadableFileIsAllowed()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "broken.json");
        await File.WriteAllTextAsync(path, "{", Encoding.UTF8);

        await repository.DeleteAsync("broken");

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("..\\outside.json")]
    [InlineData("subdir/file.json")]
    [InlineData("subdir\\file.json")]
    public async Task DeleteRejectsTraversalAndNestedPaths(string fileName)
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.DeleteAsync(fileName));
    }

    [Fact]
    public async Task DeleteCannotRemoveAbsoluteFileOutsideRoot()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out _);
        var outside = Path.Combine(directory.Path, "outside.json");
        await File.WriteAllTextAsync(outside, "keep", Encoding.UTF8);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.DeleteAsync(outside));

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task PreCancelledDeleteDoesNotRemoveFile()
    {
        using var directory = new TestDirectory();
        var repository = CreateRepository(directory, out var root);
        await repository.SaveAsync("keep", DomainTestData.CreateMacro());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.DeleteAsync("keep", cancellation.Token));

        Assert.True(File.Exists(Path.Combine(root, "keep.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-root")]
    public void RepositoryRejectsAmbiguousRoot(string root)
    {
        Assert.Throws<ArgumentException>(() => new MacroRepository(root));
    }

    [Fact]
    public void RepositoryCanonicalizesAbsoluteRoot()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "folder", "..");

        var repository = new MacroRepository(root);

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), repository.LibraryRootPath);
    }

    private static MacroRepository CreateRepository(TestDirectory directory, out string root)
    {
        root = Path.Combine(directory.Path, "library");
        return new MacroRepository(root);
    }

    private static Macro CreateEmptyMacro() => DomainTestData.CreateMacro([]) with
    {
        Recording = new RecordingMetadata(0, 0),
    };
}
