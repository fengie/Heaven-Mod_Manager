using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Crash-state reconciliation must happen after schema/migration initialization
/// and before a catalog scan can rewrite or drop a moved mod's source identity.
/// </summary>
public sealed class DuplicateArchiveStartupOrderTests
{
    [Fact]
    public void DuplicateArchiveRecoveryPrecedesCatalogDiscovery()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var app = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App", "App.xaml.cs"));
        var migration = app.IndexOf(""startup.migration"", StringComparison.Ordinal);
        var recovery = app.IndexOf(""startup.duplicate-archive.recovery"", StringComparison.Ordinal);
        var catalog = app.IndexOf(""startup.catalog.refresh"", StringComparison.Ordinal);

        Assert.True(migration >= 0, "Missing required schema/legacy migration stage.");
        Assert.True(recovery > migration, "Duplicate archive recovery must run after migration.");
        Assert.True(catalog > recovery, "Catalog refresh must not precede recovery of an interrupted archive move.");
    }
}
