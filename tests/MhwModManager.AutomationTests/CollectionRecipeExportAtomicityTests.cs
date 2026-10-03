using MhwModManager.Automation;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class CollectionRecipeExportAtomicityTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "MhwRecipeExportAtomicityTests-" + Guid.NewGuid().ToString("N"));

    public CollectionRecipeExportAtomicityTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task ExportAsyncPublishesCompleteRecipeThroughFinalPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new ManagerDatabase(Path.Combine(root, "recipe.db"));
        await db.InitializeAsync(ct);
        var destination = Path.Combine(root, "nested", "collection.json");

        var result = await new CollectionRecipeService(db).ExportAsync(destination, ct);

        Assert.Equal(Path.GetFullPath(destination), result);
        Assert.True(File.Exists(destination));
        var recipe = await new CollectionRecipeService(db).PreviewAsync(destination, ct);
        Assert.Equal(2, recipe.Recipe.Format);
        Assert.Empty(recipe.Recipe.Mods);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(destination)!,
            ".collection.json.partial-*"));
    }

    [Fact]
    public async Task AtomicWriterPreservesExistingDestinationWhenCancelledAfterTempWrite()
    {
        var destination = Path.Combine(root, "cancel.json");
        await File.WriteAllTextAsync(destination, "STABLE", TestContext.Current.CancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CollectionRecipeService.WriteTextAtomicallyAsync(
                destination,
                "REPLACEMENT",
                cts.Token,
                async (tempPath, contents, token) =>
                {
                    await File.WriteAllTextAsync(tempPath, contents, CancellationToken.None);
                    cts.Cancel();
                }));

        Assert.Equal("STABLE", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(root, ".cancel.json.partial-*"));
    }

    [Fact]
    public async Task AtomicWriterPreservesExistingDestinationAndCleansTempOnWriteFailure()
    {
        var destination = Path.Combine(root, "failure.json");
        await File.WriteAllTextAsync(destination, "STABLE", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() =>
            CollectionRecipeService.WriteTextAtomicallyAsync(
                destination,
                "REPLACEMENT",
                TestContext.Current.CancellationToken,
                async (tempPath, contents, token) =>
                {
                    await File.WriteAllTextAsync(tempPath, "PARTIAL", token);
                    throw new IOException("injected recipe export failure");
                }));

        Assert.Equal("STABLE", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(root, ".failure.json.partial-*"));
    }

    [Fact]
    public async Task AtomicWriterReplacesExistingDestinationOnlyAfterCompleteTempWrite()
    {
        var destination = Path.Combine(root, "success.json");
        await File.WriteAllTextAsync(destination, "OLD", TestContext.Current.CancellationToken);

        await CollectionRecipeService.WriteTextAtomicallyAsync(
            destination,
            "COMPLETE",
            TestContext.Current.CancellationToken);

        Assert.Equal("COMPLETE", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(root, ".success.json.partial-*"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        GC.SuppressFinalize(this);
    }
}
