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
