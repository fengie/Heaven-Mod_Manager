using System.IO.Compression;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;
using System.Text.Json;

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
    public async Task Two_independent_importers_preserve_both_receipts_after_stale_read()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var first = Path.Combine(source, "MHW First.zip");
        var second = Path.Combine(source, "NieR Automata Second.zip");
        CreateZip(first, "nativePC/pl/armor/first.tex");
        CreateZip(second, "mods/outfit.dds");

        // The first importer reads the old receipt set, then pauses before any work.
        // The second importer commits an unrelated receipt while the first is paused.
        // A naive read/modify/write of the whole JSON index then loses the second key.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new InlineProgress<GameModIntakeProgress>(update =>
        {
            if (update.Completed != 0 || update.Stage != "Inspecting") return;
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });

        var firstTask = Task.Run(
            () => CreateIntake().ImportPathsAsync([first], gate, TestToken), TestToken);
        try
        {
            await entered.Task.WaitAsync(TestToken);
            var secondReport = await CreateIntake().ImportPathsAsync([second], ct: TestToken);
            Assert.Equal(1, secondReport.Routed);
        }
        finally
        {
            release.TrySetResult();
        }

        var firstReport = await firstTask;
        Assert.Equal(1, firstReport.Routed);
        var receiptPath = Path.Combine(root, "Manager", "State", "mod-intake-receipts.json");
        var stored = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(receiptPath, TestToken));
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Length);

        var replay = await CreateIntake().ImportPathsAsync([first, second], ct: TestToken);
        Assert.Equal(0, replay.Routed);
        Assert.Equal(2, replay.AlreadyPresent);
    }

    [Fact]
    public async Task Processed_receipt_still_deduplicates_after_inbox_package_moves_elsewhere()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var first = Path.Combine(source, "MHW Outfit.zip");
        CreateZip(first, "nativePC/pl/armor/outfit.tex");

        var intake = CreateIntake();
        var result = await intake.ImportPathsAsync([first], ct: TestToken);
        Assert.Equal(1, result.Routed);
        var inbox = Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        var staged = Assert.Single(Directory.GetFiles(inbox, "*.zip"));
        var processed = Path.Combine(root, "Processed");
        Directory.CreateDirectory(processed);
        File.Move(staged, Path.Combine(processed, Path.GetFileName(staged)));

        var replay = await CreateIntake().ImportPathsAsync([first], ct: TestToken);
        Assert.Equal(0, replay.Routed);
        Assert.Equal(1, replay.AlreadyPresent);
        Assert.Empty(Directory.GetFiles(inbox, "*.zip"));
        Assert.True(File.Exists(first));
    }

    [Fact]
    public async Task Competing_importers_of_identical_archive_bytes_publish_only_one_package()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var first = Path.Combine(source, "MHW Armor A.zip");
        var second = Path.Combine(source, "MHW Armor B.zip");
        CreateZip(first, "nativePC/pl/armor/armor.tex");
        File.Copy(first, second);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pause = new InlineProgress<GameModIntakeProgress>(update =>
        {
            if (update.Completed != 0 || update.Stage != "Inspecting") return;
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });
        var firstTask = Task.Run(
            () => CreateIntake().ImportPathsAsync([first], pause, TestToken), TestToken);
        try
        {
            await entered.Task.WaitAsync(TestToken);
            var secondResult = await CreateIntake().ImportPathsAsync([second], ct: TestToken);
            Assert.Equal(1, secondResult.Routed);
        }
        finally { release.TrySetResult(); }

        var firstResult = await firstTask;
        Assert.Equal(0, firstResult.Routed);
        Assert.Equal(1, firstResult.AlreadyPresent);
        var inbox = Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        Assert.Single(Directory.GetFiles(inbox, "*.zip"));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task Receipt_writer_lock_wait_can_be_cancelled_without_publishing()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "MHW Armor.zip");
        CreateZip(archive, "nativePC/pl/armor/armor.tex");
        var state = Path.Combine(root, "Manager", "State");
        Directory.CreateDirectory(state);
        await using var heldLock = new FileStream(Path.Combine(state, "mod-intake-receipts.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateIntake().ImportPathsAsync([archive], ct: cancel.Token));

        Assert.True(File.Exists(archive));
        var inbox = Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        Assert.Empty(Directory.GetFiles(inbox, "*.zip"));
        Assert.False(File.Exists(Path.Combine(state, "mod-intake-receipts.json")));
    }

    [Fact]
    public async Task Cancel_during_archive_byte_copy_cleans_owned_scratch_and_never_commits()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "MHW Large.zip");
        var payload = new byte[4 * 1024 * 1024];
        new Random(47).NextBytes(payload);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("nativePC/pl/armor/large.tex").Open())
            await entry.WriteAsync(payload, TestToken);

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var copyUpdates = new List<GameModIntakeProgress>();
        var progress = new InlineProgress<GameModIntakeProgress>(update =>
        {
            if (!update.Stage.StartsWith("Copying ", StringComparison.Ordinal)) return;
            copyUpdates.Add(update);
            cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateIntake().ImportPathsAsync([archive], progress, cancel.Token));

        Assert.NotEmpty(copyUpdates);
        Assert.True(File.Exists(archive));
        var inbox = Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        Assert.Empty(Directory.GetFiles(inbox, "*.zip"));
        Assert.False(File.Exists(Path.Combine(root, "Manager", "State", "mod-intake-receipts.json")));
    }

    [Fact]
    public async Task Retry_after_receipt_write_failure_adopts_intact_staged_archive()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "MHW Recovery.zip");
        CreateZip(archive, "nativePC/pl/armor/recovery.tex");

        var state = Path.Combine(root, "Manager", "State");
        Directory.CreateDirectory(state);
        // A path collision blocks only the receipt index's final rename.
        // The already-moved staged package is still recoverable on retry.
        var blockedReceipt = Path.Combine(state, "mod-intake-receipts.json");
        Directory.CreateDirectory(blockedReceipt);
        var intake = CreateIntake();

        var failed = await intake.ImportPathsAsync([archive], ct: TestToken);
        Assert.Equal(1, failed.NeedsReview);
        Assert.Equal(0, failed.Routed);
        var inbox = Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox");
        var staged = Assert.Single(Directory.GetFiles(inbox, "*.zip"));
        Assert.True(File.Exists(staged));
        Assert.True(File.Exists(archive));

        Directory.Delete(blockedReceipt);
        var replay = await CreateIntake().ImportPathsAsync([archive], ct: TestToken);
        Assert.Equal(1, replay.AlreadyPresent);
        Assert.Equal(0, replay.Routed);
        Assert.Single(Directory.GetFiles(inbox, "*.zip"));
        Assert.True(File.Exists(Path.Combine(state, "mod-intake-receipts.json")));
    }

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
        await File.WriteAllTextAsync(Path.Combine(source, "Bad.7z"), "not a 7z archive", TestToken);
        CreateZip(Path.Combine(source, "NieR Automata Outfit.zip"), "mods/outfit.dds");

        var intake = CreateIntake();
        var report = await intake.ImportPathsAsync([source], ct: TestToken);

        Assert.Equal(2, report.Routed);
        Assert.Equal(2, report.NeedsReview);
        Assert.Equal(4, Directory.GetFiles(source).Length);
        Assert.True(File.Exists(intake.ReportPath));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Manager", "Mods by Game", "monster-hunter-world", "Inbox")));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Manager", "Mods by Game", "nier-automata", "Inbox")));
    }

    [Fact]
    public async Task Identical_archive_bytes_at_different_source_paths_are_deduplicated_per_game()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var first = Path.Combine(source, "MHW Armor A.zip");
        var identical = Path.Combine(source, "MHW Armor B.zip");
        CreateZip(first, "nativePC/pl/armor/armor.tex");
        File.Copy(first, identical);

        var intake = CreateIntake();
        var report = await intake.ImportPathsAsync([first, identical], ct: TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal(1, report.AlreadyPresent);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(identical));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Manager",
            "Mods by Game", "monster-hunter-world", "Inbox"), "*.zip"));
    }

    [Fact]
    public async Task Unsafe_zip_entry_does_not_block_an_unrelated_valid_archive()
    {
        var source = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(source);
        var malicious = Path.Combine(source, "MHW Unsafe.zip");
        using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create))
            WriteEntry(zip, "../escape.txt");
        var good = Path.Combine(source, "MHW Good.zip");
        CreateZip(good, "nativePC/pl/armor/valid.tex");

        var intake = CreateIntake();
        var report = await intake.ImportPathsAsync([malicious, good], ct: TestToken);

        Assert.Equal(1, report.Routed);
        Assert.Equal(1, report.NeedsReview);
        Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        Assert.True(File.Exists(malicious));
        Assert.True(File.Exists(good));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Manager",
            "Mods by Game", "monster-hunter-world", "Inbox"), "*.zip"));
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
