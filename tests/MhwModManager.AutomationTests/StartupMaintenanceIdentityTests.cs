using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

/// <summary>
/// SQLite mod IDs use binary primary-key equality. Two IDs that differ only
/// by case are independent records; maintenance must not combine or retire
/// their file rows through a case-insensitive dictionary.
/// </summary>
public sealed class StartupMaintenanceIdentityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "umm-identity-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public StartupMaintenanceIdentityTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task CategoryAssignmentKeepsCaseDistinctModRecordsIndependent()
    {
        var db = await CreateDatabaseAsync("category-identity.db");
        await db.UpsertModAsync(new("Alpha", "Upper", "Upper", Path.Combine(root, "upper"), false, 1), Token);
        await db.UpsertModAsync(new("alpha", "Lower", "Lower", Path.Combine(root, "lower"), false, 2), Token);
        await db.ReplaceModFilesAsync("Alpha",
            [ModFile("Alpha", @"root\plugins\loader.dll", "upper", FileClass.Plugin)], Token);
        await db.ReplaceModFilesAsync("alpha",
            [ModFile("alpha", @"nativePC\pl\f_equip\f_body.mod3", "lower", FileClass.Structural)], Token);

        Assert.Equal(2, await new AutoCategoryService(db).AssignMissingAsync(Token));
        var mods = (await db.GetModsAsync(Token)).ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        Assert.Equal("Plugin", mods["Alpha"].Category);
        Assert.Equal("Armor", mods["alpha"].Category);
    }

    [Fact]
    public async Task DuplicateFingerprintScanKeepsCaseDistinctModRecordsIndependent()
    {
        var db = await CreateDatabaseAsync("duplicate-identity.db");
        await db.UpsertModAsync(new("Mod", "Upper", "Upper", Path.Combine(root, "upper"), false, 1), Token);
        await db.UpsertModAsync(new("mod", "Lower", "Lower", Path.Combine(root, "lower"), false, 2), Token);
        await db.ReplaceModFilesAsync("Mod",
            [ModFile("Mod", @"nativePC\shared.tex", "first-sha", FileClass.Texture)], Token);
        await db.ReplaceModFilesAsync("mod",
            [ModFile("mod", @"nativePC\shared.tex", "second-sha", FileClass.Texture)], Token);

        var analysis = await new DuplicateCleanupService(db, Path.Combine(root, "archive")).AnalyzeAsync(Token);
        Assert.Empty(analysis.ExactDuplicates);
        Assert.Empty(analysis.SupersededModIds);
    }

    [Fact]
    public async Task CaseDistinctExactDuplicatesRetireOnlyNonkeeperAndPreserveFiles()
    {
        var db = await CreateDatabaseAsync("duplicate-retirement.db");
        var older = Path.Combine(root, "pair-original");
        var keeper = Path.Combine(root, "pair-keeper");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(keeper);
        await File.WriteAllTextAsync(Path.Combine(older, "sentinel.txt"), "original", Token);
        await File.WriteAllTextAsync(Path.Combine(keeper, "sentinel.txt"), "original", Token);
        await db.UpsertModAsync(new("Pair", "Older", "Older", older, false, 1), Token);
        await db.UpsertModAsync(new("pair", "Keeper", "Keeper", keeper, false, 2), Token);
        await db.ReplaceModFilesAsync("Pair",
            [ModFile("Pair", @"nativePC\shared.tex", "identical-sha", FileClass.Texture)], Token);
        await db.ReplaceModFilesAsync("pair",
            [ModFile("pair", @"nativePC\shared.tex", "identical-sha", FileClass.Texture)], Token);

        var archiveRoot = Path.Combine(root, "archive-retired");
        var outcome = await new DuplicateCleanupService(db, archiveRoot).ArchiveSafeWithAnalysisAsync(Token);
        Assert.Equal(1, outcome.Archived);
        Assert.Empty(outcome.Analysis.ExactDuplicates);
        var remaining = Assert.Single(await db.GetModsAsync(Token));
        Assert.Equal("pair", remaining.Id);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(keeper, "sentinel.txt"), Token));
        Assert.False(Directory.Exists(older));
        Assert.Equal("original",
            await File.ReadAllTextAsync(Path.Combine(Assert.Single(Directory.GetDirectories(archiveRoot)), "sentinel.txt"), Token));
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync(string fileName)
    {
        var db = new ManagerDatabase(Path.Combine(root, fileName));
        await db.InitializeAsync(Token);
        return db;
    }

    private static ModFileDescriptor ModFile(string modId, string path, string sha, FileClass fileClass) =>
        new(modId, path, sha, null, 1, DateTimeOffset.UtcNow, fileClass);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
