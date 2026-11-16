using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Fault-isolation and cancellation contracts for imported archives. All fixtures are
/// generated offline in a unique temp directory; no game installation is touched.
/// </summary>
public sealed class BulkImportFaultBoundaryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "umm-intake-faults-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private IReadOnlyList<GameProfile> Games =>
    [
        GameProfile.MonsterHunterWorld(Path.Combine(root, "Games", "MHW")),
        GameProfile.Generic("nier-automata", "NieR Automata",
            Path.Combine(root, "Games", "NieR"), "NieRAutomata.exe", "mods")
    ];

    private GameModIntakeService CreateIntake() =>
        new(Path.Combine(root, "Manager"), Path.Combine(root, "Manager", "State"),
            () => Games);

    [Fact]
    public async Task Empty_bulk_selection_is_a_no_op_without_creating_manager_state()
    {
        var intake = CreateIntake();
        var result = await intake.ImportPathsAsync([], ct: TestToken);

        Assert.Empty(result.Items);
        Assert.False(Directory.Exists(Path.Combine(root, "Manager")));
    }

    [Fact]
    public async Task Too_many_selected_paths_are_rejected_before_any_source_is_imported()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var good = Path.Combine(source, "MHW Armor.zip");
        CreateZip(good, "nativePC/pl/armor/armor.tex");

        var paths = new[] { good }.Concat(Enumerable.Range(0, 2048)
            .Select(i => Path.Combine(source, $"missing-{i:D4}.zip")));
        var intake = CreateIntake();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => intake.ImportPathsAsync(paths, ct: TestToken));

        Assert.True(File.Exists(good));
        Assert.False(Directory.Exists(Path.Combine(root, "Manager", "Mods by Game")));
    }

    [Fact]
    public async Task Invalid_receipt_index_fails_closed_before_staging_new_mods()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var good = Path.Combine(source, "MHW Armor.zip");
        CreateZip(good, "nativePC/pl/armor/armor.tex");

        var state = Path.Combine(root, "Manager", "State");
        Directory.CreateDirectory(state);
        var receipts = Path.Combine(state, "mod-intake-receipts.json");
        await File.WriteAllTextAsync(receipts, "[\"not-a-sha256\"]", TestToken);
        var intake = CreateIntake();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => intake.ImportPathsAsync([good], ct: TestToken));

        Assert.True(File.Exists(good));
        Assert.Equal("[\"not-a-sha256\"]", await File.ReadAllTextAsync(receipts, TestToken));
        Assert.False(Directory.Exists(Path.Combine(root, "Manager", "Mods by Game")));
    }

    [Fact]
    public async Task Cancellation_after_one_completed_archive_preserves_sources_and_skips_later_archives()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var first = Path.Combine(source, "MHW Armor A.zip");
        var second = Path.Combine(source, "MHW Armor B.zip");
        CreateZip(first, "nativePC/pl/armor/first.tex");
        CreateZip(second, "nativePC/pl/armor/second.tex");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var updates = new List<GameModIntakeProgress>();
        var progress = new InlineProgress<GameModIntakeProgress>(update =>
        {
            updates.Add(update);
            if (update.Completed == 1 && update.Stage == "Completed")
                cancel.Cancel();
        });
        var intake = CreateIntake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => intake.ImportPathsAsync([first, second], progress, cancel.Token));

        var inbox = Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        Assert.Single(Directory.GetFiles(inbox, "*.zip"));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Contains(updates, item => item.Completed == 1 && item.Stage == "Completed");
        Assert.DoesNotContain(updates, item => item.Completed == 2);
        Assert.False(File.Exists(intake.ReportPath));
    }

    [Fact]
    public async Task Archive_only_folder_isolates_invalid_archive_without_losing_valid_siblings()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        CreateZip(Path.Combine(source, "MHW Armor A.zip"), "nativePC/pl/armor/a.tex");
        await File.WriteAllTextAsync(Path.Combine(source, "Bad.rar"), "not an archive", TestToken);
        CreateZip(Path.Combine(source, "NieR Automata Outfit.zip"), "mods/outfit.dds");

        var intake = CreateIntake();
        var report = await intake.ImportPathsAsync([source], ct: TestToken);

        Assert.Equal(2, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.Equal(3, Directory.GetFiles(source).Length);
        Assert.True(File.Exists(intake.ReportPath));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Manager", "Mods by Game", "nier-automata", "Inbox")));
    }

    [Fact]
    public async Task Archive_case_folded_duplicate_output_paths_fail_before_writing_any_payload()
    {
        var archive = Path.Combine(root, "duplicates.zip");
        Directory.CreateDirectory(root);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "nativePC/pl/armor/Part.tex");
            WriteEntry(zip, "nativePC/pl/armor/part.tex");
        }

        var trusted = Path.Combine(root, "trusted");
        Directory.CreateDirectory(trusted);
        var destination = Path.Combine(trusted, "owned");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new ArchiveInspector().ExtractSafelyAsync(archive, destination, trusted, TestToken));

        Assert.False(Directory.Exists(destination));
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task File_ancestor_collision_in_archive_fails_without_partial_extraction()
    {
        var archive = Path.Combine(root, "colliding-paths.zip");
        Directory.CreateDirectory(root);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "nativePC/pl/armor");
            WriteEntry(zip, "nativePC/pl/armor/child.tex");
        }

        var trusted = Path.Combine(root, "trusted");
        Directory.CreateDirectory(trusted);
        var destination = Path.Combine(trusted, "owned");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new ArchiveInspector().ExtractSafelyAsync(archive, destination, trusted, TestToken));

        Assert.False(Directory.Exists(destination));
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task Cancellation_during_extraction_removes_partially_created_owned_file()
    {
        var archive = Path.Combine(root, "large.zip");
        Directory.CreateDirectory(root);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("nativePC/pl/armor/payload.tex").Open())
        {
            await entry.WriteAsync(new byte[256 * 1024], TestToken);
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var inspector = new ArchiveInspector((point, _) =>
        {
            if (point == ArchiveExtractionFaultPoint.BeforePayloadWrite)
                cancel.Cancel();
        });
        var trusted = Path.Combine(root, "trusted");
        Directory.CreateDirectory(trusted);
        var destination = Path.Combine(trusted, "owned");
        var extracted = Path.Combine(destination, "nativePC", "pl", "armor", "payload.tex");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inspector.ExtractSafelyAsync(archive, destination, trusted, cancel.Token));

        Assert.False(File.Exists(extracted));
        Assert.True(File.Exists(archive));
    }

    private static void CreateZip(string path, string entry)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, entry);
    }

    private static void WriteEntry(ZipArchive zip, string entry)
    {
        using var stream = zip.CreateEntry(entry).Open();
        stream.WriteByte(42);
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
