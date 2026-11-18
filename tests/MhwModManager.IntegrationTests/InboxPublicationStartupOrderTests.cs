using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Persisted Inbox publication intents must be reconciled before catalog indexing.
/// Otherwise an interrupted published destination can be re-imported as a new mod.
/// </summary>
public sealed class InboxPublicationStartupOrderTests
{
    [Fact]
    public void InboxRecoveryPrecedesCatalogDiscoveryAndAutomation()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var app = File.ReadAllText(Path.Combine(root.FullName, "src", "MhwModManager.App", "App.xaml.cs"));
        var duplicateRecovery = app.IndexOf("startup.duplicate-archive.recovery", StringComparison.Ordinal);
        var inboxRecovery = app.IndexOf("startup.inbox-publication.recovery", StringComparison.Ordinal);
        var catalog = app.IndexOf("startup.catalog.refresh", StringComparison.Ordinal);
        var automaticInbox = app.IndexOf("startup.automation", StringComparison.Ordinal);

        Assert.True(duplicateRecovery >= 0, "Missing mandatory duplicate recovery.");
        Assert.True(inboxRecovery > duplicateRecovery, "Inbox recovery must follow schema and duplicate recovery.");
        Assert.True(catalog > inboxRecovery, "Catalog must not index an interrupted Inbox publish before reconciliation.");
        Assert.True(automaticInbox > catalog, "Automatic Inbox processing must follow reconciliation and catalog refresh.");
    }
}
