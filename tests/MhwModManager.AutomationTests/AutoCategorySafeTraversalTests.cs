using System.Diagnostics;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

/// <summary>
/// Uncaptured source directories are only advisory category evidence. A junction
/// must not let files outside the mod source influence persisted classification.
/// </summary>
public sealed class AutoCategorySafeTraversalTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "umm-autocat-containment-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> junctions = [];
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public AutoCategorySafeTraversalTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task DescendantJunctionCannotClassifyFromExternalFilesOrBlockOtherMods()
    {
        var db = await CreateDatabaseAsync("descendant.db");
        var source = Path.Combine(root, "uncaptured");
        var outside = Path.Combine(root, "external");
        var safeSource = Path.Combine(root, "safe-mod");
        Directory.CreateDirectory(Path.Combine(source, "nativePC"));
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(safeSource, "nativePC", "pl", "f_equip"));
        await File.WriteAllTextAsync(Path.Combine(outside, "npc-mask.txt"), "outside", Token);
        await File.WriteAllTextAsync(Path.Combine(safeSource, "nativePC", "pl", "f_equip", "armor.mod3"), "safe", Token);
        CreateDirectoryJunction(Path.Combine(source, "nativePC", "external"), outside);

        await db.UpsertModAsync(new("unsafe", "Unsafe", "Unsafe", source, false, 0), Token);
        await db.UpsertModAsync(new("safe", "Safe", "Safe", safeSource, false, 1), Token);

        var assigned = await new AutoCategoryService(db).AssignMissingAsync(Token);
        var mods = (await db.GetModsAsync(Token)).ToDictionary(m => m.Id, StringComparer.Ordinal);
        Assert.Equal(1, assigned);
        Assert.Null(mods["unsafe"].Category);
        Assert.Equal(AutomationCategory.Armor.ToString(), mods["safe"].Category);
        Assert.Equal("outside", await File.ReadAllTextAsync(Path.Combine(outside, "npc-mask.txt"), Token));
    }

    [Fact]
    public async Task JunctionAsSourceRootCannotSupplyCategoryEvidence()
    {
        var db = await CreateDatabaseAsync("source-root.db");
        var outside = Path.Combine(root, "outside-root");
        var linkedSource = Path.Combine(root, "source-junction");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "npc-skin.txt"), "outside", Token);
        CreateDirectoryJunction(linkedSource, outside);
        await db.UpsertModAsync(new("linked", "Linked", "Linked", linkedSource, false, 0), Token);

        Assert.Equal(0, await new AutoCategoryService(db).AssignMissingAsync(Token));
        Assert.Null(Assert.Single(await db.GetModsAsync(Token)).Category);
        Assert.Equal("outside", await File.ReadAllTextAsync(Path.Combine(outside, "npc-skin.txt"), Token));
    }

    [Fact]
    public async Task IndexedFileRowsBypassUnsafeSourceFolderAndKeepAuthority()
    {
        var db = await CreateDatabaseAsync("indexed.db");
        var outside = Path.Combine(root, "outside-indexed");
        var linkedSource = Path.Combine(root, "indexed-source-junction");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "npc.txt"), "outside", Token);
        CreateDirectoryJunction(linkedSource, outside);
        await db.UpsertModAsync(new("indexed", "Indexed", "Indexed", linkedSource, false, 0), Token);
        await db.ReplaceModFilesAsync("indexed",
            [new ModFileDescriptor("indexed", @"root\plugins\loader.dll", "blob-sha", null, 12, DateTimeOffset.UtcNow, FileClass.Plugin)], Token);

        Assert.Equal(1, await new AutoCategoryService(db).AssignMissingAsync(Token));
        Assert.Equal(AutomationCategory.Plugin.ToString(),
            Assert.Single(await db.GetModsAsync(Token)).Category);
    }

    private async Task<ManagerDatabase> CreateDatabaseAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name));
        await db.InitializeAsync(Token);
        return db;
    }

    private void CreateDirectoryJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(info)
            ?? throw new IOException("Could not launch junction setup command.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException($"mklink failed: {process.StandardError.ReadToEnd()}");
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        junctions.Add(link);
    }

    public void Dispose()
    {
        foreach (var junction in junctions)
        {
            try { if (Directory.Exists(junction)) Directory.Delete(junction); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
