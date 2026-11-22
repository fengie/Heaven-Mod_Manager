using System.Diagnostics;
using System.Text.Json;
using MhwModManager.Automation;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ImportWorkspaceLeaseRecoveryTests
{
    [Fact]
    public void Successful_publication_removes_only_lease_and_preserves_mod_payload()
    {
        using var temp = new TemporaryWorkspace();
        var staging = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "archive");
        var leasePath = staging + ImportWorkspaceMaintenance.LeaseSuffix;
        Assert.True(File.Exists(leasePath));

        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "payload.txt"), "PRESERVE");
        var destination = Path.Combine(temp.ModsRoot, "new-package");

        ImportPublicationWorkspace.Publish(temp.ModsRoot, staging, destination);
        ImportPublicationWorkspace.Cleanup(temp.ModsRoot, staging);

        Assert.False(File.Exists(leasePath));
        Assert.False(Directory.Exists(staging));
        Assert.Equal("PRESERVE", File.ReadAllText(Path.Combine(destination, "payload.txt")));
    }

    [Fact]
    public void Successful_inbox_publication_does_not_leave_a_lease_when_caller_clears_staging()
    {
        using var temp = new TemporaryWorkspace();
        var staging = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "inbox");
        var leasePath = staging + ImportWorkspaceMaintenance.LeaseSuffix;
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "payload.txt"), "INBOX");

        var destination = Path.Combine(temp.ModsRoot, "inbox-package");
        ImportPublicationWorkspace.Publish(temp.ModsRoot, staging, destination);

        // Smart Inbox clears its staging variable immediately after Publish.
        Assert.False(Directory.Exists(staging));
        Assert.False(File.Exists(leasePath));
        Assert.Equal("INBOX", File.ReadAllText(Path.Combine(destination, "payload.txt")));
    }

    [Fact]
    public void Dead_owner_work_is_reclaimed_after_grace_period_but_unleased_work_is_preserved()
    {
        using var temp = new TemporaryWorkspace();
        var staging = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "fomod-source");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "unfinished.txt"), "SCRATCH");
        MakeOwnerExited(staging);

        var unknown = Path.Combine(ImportPublicationWorkspace.RootFor(temp.ModsRoot), "archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unknown);
        var sentinel = Path.Combine(unknown, "unknown.txt");
        File.WriteAllText(sentinel, "KEEP");

        var result = ImportWorkspaceMaintenance.CleanupRoot(
            temp.ModsRoot, DateTimeOffset.UtcNow.AddHours(2), TimeSpan.FromHours(1),
            long.MaxValue, null, CancellationToken.None);

        Assert.False(Directory.Exists(staging));
        Assert.False(File.Exists(staging + ImportWorkspaceMaintenance.LeaseSuffix));
        Assert.Equal(1, result.DeletedWorkspaces);
        Assert.True(File.Exists(sentinel));
        Assert.True(result.DeferredEntries >= 1);
    }

    [Fact]
    public void Active_owner_and_unverifiable_owner_never_become_quota_deletion_targets()
    {
        using var temp = new TemporaryWorkspace();
        var active = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "archive");
        Directory.CreateDirectory(active);
        File.WriteAllText(Path.Combine(active, "active.bin"), "KEEP");

        var unknown = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "fomod-install");
        Directory.CreateDirectory(unknown);
        File.WriteAllText(Path.Combine(unknown, "unknown.bin"), "KEEP");
        var path = unknown + ImportWorkspaceMaintenance.LeaseSuffix;
        var lease = JsonSerializer.Deserialize<ImportWorkspaceLease>(File.ReadAllText(path))!;
        File.WriteAllText(path, JsonSerializer.Serialize(lease with { OwnerProcessStartUtc = null }));

        var result = ImportWorkspaceMaintenance.CleanupRoot(
            temp.ModsRoot, DateTimeOffset.UtcNow.AddDays(7), TimeSpan.Zero,
            maxOwnedBytes: 0, log: null, CancellationToken.None);

        Assert.Equal(0, result.DeletedWorkspaces);
        Assert.True(File.Exists(Path.Combine(active, "active.bin")));
        Assert.True(File.Exists(Path.Combine(unknown, "unknown.bin")));
        Assert.True(result.DeferredEntries >= 2);
    }

    [Fact]
    public void Recently_abandoned_owned_scratch_is_reclaimed_when_over_its_byte_quota()
    {
        using var temp = new TemporaryWorkspace();
        var staging = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "archive");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "large.bin"), new byte[4096]);
        MakeOwnerExited(staging);

        var result = ImportWorkspaceMaintenance.CleanupRoot(
            temp.ModsRoot, DateTimeOffset.UtcNow, TimeSpan.FromHours(1),
            maxOwnedBytes: 1024, log: null, CancellationToken.None);

        Assert.Equal(1, result.DeletedWorkspaces);
        Assert.True(result.ReclaimedBytes >= 4096);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void Nested_reparse_and_malformed_lease_preserve_external_and_unverifiable_data()
    {
        using var temp = new TemporaryWorkspace();
        var external = Path.Combine(temp.Root, "external");
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "keep.txt");
        File.WriteAllText(sentinel, "KEEP");

        var staging = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "archive");
        Directory.CreateDirectory(staging);
        MakeOwnerExited(staging);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(staging, "link"), external);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Hosts without symlink privileges cannot test the reparse branch.
            return;
        }

        var malformed = ImportPublicationWorkspace.Allocate(temp.ModsRoot, "fomod-source");
        Directory.CreateDirectory(malformed);
        File.WriteAllText(Path.Combine(malformed, "payload.bin"), "KEEP");
        File.WriteAllText(malformed + ImportWorkspaceMaintenance.LeaseSuffix, "not-json");

        var result = ImportWorkspaceMaintenance.CleanupRoot(
            temp.ModsRoot, DateTimeOffset.UtcNow.AddDays(2), TimeSpan.Zero,
            maxOwnedBytes: 0, log: null, CancellationToken.None);

        Assert.True(File.Exists(sentinel));
        Assert.True(Directory.Exists(staging));
        Assert.True(Directory.Exists(malformed));
        Assert.True(result.DeferredEntries >= 2);
    }


    [Fact]
    public async Task Forcibly_terminated_import_worker_reclaims_its_durable_lease_without_deleting_user_content()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryWorkspace();
        var installed = Path.Combine(temp.ModsRoot, "user-package");
        Directory.CreateDirectory(installed);
        var userSentinel = Path.Combine(installed, "keep.txt");
        File.WriteAllText(userSentinel, "USER-DATA");
        var workspace = ImportPublicationWorkspace.RootFor(temp.ModsRoot);
        var unknown = Path.Combine(workspace, "legacy-unleased");
        Directory.CreateDirectory(unknown);
        var unknownSentinel = Path.Combine(unknown, "keep.txt");
        File.WriteAllText(unknownSentinel, "UNKNOWN-DATA");

        // Invoke the actual xUnit test executable in a separate process, using
        // the production Allocate path. Kill it after its lease and partial
        // payload are durable, before any finally/normal cleanup can run.
        var executable = Path.Combine(AppContext.BaseDirectory, "MhwModManager.IntegrationTests.exe");
        Assert.True(File.Exists(executable), "The integration test executable is required for crash proof.");
        var ready = Path.Combine(temp.Root, "worker-ready.txt");
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--filter-method");
        psi.ArgumentList.Add(
            "MhwModManager.IntegrationTests.ImportWorkspaceLeaseRecoveryTests.Crash_worker_allocates_and_waits");
        psi.Environment["MHW_TEST_IMPORT_CRASH_ROOT"] = temp.Root;
        using var worker = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start the disposable import crash fixture.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(ready))
            {
                Assert.False(worker.HasExited,
                    "Import crash worker exited before publishing its durable fixture marker.");
                await Task.Delay(50, timeout.Token);
            }

            var staging = Path.GetFullPath(File.ReadAllText(ready));
            Assert.Equal(workspace, Path.GetDirectoryName(staging), ignoreCase: true);
            var leasePath = staging + ImportWorkspaceMaintenance.LeaseSuffix;
            Assert.True(File.Exists(leasePath));
            Assert.Equal("PARTIAL", File.ReadAllText(Path.Combine(staging, "partial.bin")));
            var lease = JsonSerializer.Deserialize<ImportWorkspaceLease>(
                File.ReadAllText(leasePath));
            Assert.NotNull(lease);
            Assert.Equal(worker.Id, lease.OwnerProcessId);
            Assert.NotNull(lease.OwnerProcessStartUtc);
            Assert.False(worker.HasExited);

            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync(timeout.Token);
            Assert.True(worker.HasExited);
            Assert.True(File.Exists(leasePath));

            var reclaimed = ImportWorkspaceMaintenance.CleanupRoot(
                temp.ModsRoot, DateTimeOffset.UtcNow.AddHours(2),
                TimeSpan.FromHours(1), long.MaxValue, null,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, reclaimed.DeletedWorkspaces);
            Assert.Equal(1, reclaimed.DeletedLeases);
            Assert.True(reclaimed.ReclaimedBytes >= "PARTIAL".Length);
            Assert.False(Directory.Exists(staging));
            Assert.False(File.Exists(leasePath));
            Assert.Equal("USER-DATA", File.ReadAllText(userSentinel));
            Assert.Equal("UNKNOWN-DATA", File.ReadAllText(unknownSentinel));
        }
        finally
        {
            if (!worker.HasExited)
            {
                try { worker.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }

    [Fact]
    public async Task Crash_worker_allocates_and_waits()
    {
        var root = Environment.GetEnvironmentVariable("MHW_TEST_IMPORT_CRASH_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;

        var modsRoot = Path.Combine(root, "Mods by Game", "generic-game", "Installed");
        var staging = ImportPublicationWorkspace.Allocate(modsRoot, "archive");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "partial.bin"), "PARTIAL");
        var ready = Path.Combine(root, "worker-ready.txt");
        var temporary = ready + ".tmp";
        File.WriteAllText(temporary, staging);
        File.Move(temporary, ready);
        // The parent forcibly terminates this process. No normal finally
        // executes to retire the lease or remove incomplete extraction.
        await Task.Delay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
    }

    private static void MakeOwnerExited(string staging)
    {
        var path = staging + ImportWorkspaceMaintenance.LeaseSuffix;
        var original = JsonSerializer.Deserialize<ImportWorkspaceLease>(File.ReadAllText(path))!;
        File.WriteAllText(path, JsonSerializer.Serialize(original with
        {
            OwnerProcessId = int.MaxValue,
            OwnerProcessStartUtc = DateTimeOffset.UtcNow.AddDays(-1)
        }));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "mhwmm-owned-import-" + Guid.NewGuid().ToString("N"));
        public string ModsRoot => Path.Combine(Root, "Mods by Game", "generic-game", "Installed");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
