using MhwModManager.App;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DeploymentOrderingTests
{
    public static TheoryData<string> GameIds => DiscoveredGameLifecycleTests.ProfileIds;

    private sealed record Fixture(AppServices Services, string Source, string Target)
    {
        public async Task<DeploymentPlan> Preview(bool enabled, CancellationToken ct)
        {
            // Reopen the persistent state rather than reusing an earlier in-memory snapshot.
            var db = new ManagerDatabase(Services.Paths.DatabasePath);
            var state = await new PlannerSnapshotRepository(db).LoadAsync(ct);
            return Services.Planner.Build(state with { Mods = state.Mods.Select(mod => mod with { Enabled = enabled }).ToArray() });
        }

        public async Task Apply(bool enabled, CancellationToken ct)
        {
            var plan = await Preview(enabled, ct);
            var target = new Dictionary<string, (bool, int)> { ["outfit"] = (enabled, 5) };
            var result = await Services.Executor.ApplyAsync(plan, "Apply staged choices", target, ct:ct);
            Assert.True(result.Success, result.Message);
        }

        public async Task Check(bool enabled, CancellationToken ct)
        {
            var state = await Services.PlannerSnapshots.LoadAsync(ct);
            Assert.Equal(enabled, Assert.Single(state.Mods).Enabled);
            Assert.Equal(enabled ? 1 : 0, state.CurrentManifest.Count);
            Assert.Equal(enabled, File.Exists(Target));
            if (enabled) Assert.Equal("outfit bytes", await File.ReadAllTextAsync(Target, ct));
            Assert.Equal("outfit bytes", await File.ReadAllTextAsync(Source, ct));
        }
    }

    private static Task Run(string id, Func<Fixture,CancellationToken,Task> exercise) =>
        IsolatedWpfSession.RunAsync(async (s,ct) =>
        {
            var directory = Directory.CreateDirectory(Path.Combine(s.Paths.ToolRoot, "source")).FullName;
            var source = Path.Combine(directory,s.Paths.Game.AdapterId == "unreal-paks" ? "outfit.pak" : "outfit.bin");
            await File.WriteAllTextAsync(source,"outfit bytes",ct);
            var mod = new ModDescriptor("outfit","Outfit","Outfit",directory,false,5);
            await s.Database.UpsertModAsync(mod,ct);
            var file = Assert.Single(await s.Scanner.CaptureAsync(mod,ct));
            var fixture = new Fixture(s,source,s.Executor.Destination(file.Path));
            Assert.False((await s.Executor.UndoLastAsync(ct)).Success);
            Assert.Single((await fixture.Preview(true,ct)).Changes);
            Assert.Single((await fixture.Preview(true,ct)).Changes);
            await fixture.Check(false,ct);
            await exercise(fixture,ct);
        },DiscoveredGameProfiles.Load().Single(game => game.Id == id));

    [Theory, MemberData(nameof(GameIds))]
    public Task RepeatedApplyPreservesTheLastActualUndo(string id) => Run(id,async (f,ct) =>
    {
        var revision = f.Services.Executor.CommittedFileRevision;
        await f.Apply(true,ct);
        Assert.Equal(revision + 1, f.Services.Executor.CommittedFileRevision);
        await f.Check(true,ct);
        await f.Apply(true,ct);
        Assert.Equal(revision + 1, f.Services.Executor.CommittedFileRevision);
        Assert.True((await f.Services.Executor.UndoLastAsync(ct)).Success);
        Assert.Equal(revision + 2, f.Services.Executor.CommittedFileRevision);
        await f.Check(false,ct);
        Assert.False((await f.Services.Executor.UndoLastAsync(ct)).Success);
    });

    [Theory, MemberData(nameof(GameIds))]
    public Task ConsecutiveUndosWalkTheActualDeploymentHistory(string id) => Run(id,async (f,ct) =>
    {
        await f.Apply(true,ct);
        await f.Apply(false,ct);
        await f.Check(false,ct);
        Assert.True((await f.Services.Executor.UndoLastAsync(ct)).Success);
        await f.Check(true,ct);
        Assert.True((await f.Services.Executor.UndoLastAsync(ct)).Success);
        await f.Check(false,ct);
        Assert.False((await f.Services.Executor.UndoLastAsync(ct)).Success);
    });

    [Theory, MemberData(nameof(GameIds))]
    public Task UndoAfterRemovalPreservesNewExternalFile(string id) => Run(id,async (f,ct) =>
    {
        await f.Apply(true,ct);
        await f.Apply(false,ct);
        Directory.CreateDirectory(Path.GetDirectoryName(f.Target)!);
        await File.WriteAllTextAsync(f.Target,"external bytes",ct);
        var undo = await f.Services.Executor.UndoLastAsync(ct);
        Assert.False(undo.Success);
        Assert.Equal("external bytes",await File.ReadAllTextAsync(f.Target,ct));
        Assert.False(Assert.Single(await f.Services.Database.GetModsAsync(ct)).Enabled);
        Assert.Empty((await f.Services.PlannerSnapshots.LoadAsync(ct)).CurrentManifest);
        File.Delete(f.Target);
        Assert.True((await f.Services.Executor.UndoLastAsync(ct)).Success);
        await f.Check(true,ct);
    });
}
