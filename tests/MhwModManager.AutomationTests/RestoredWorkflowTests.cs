using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class RestoredWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhw-restored-workflows-" + Guid.NewGuid().ToString("N"));

    public RestoredWorkflowTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task FomodPlansOnlySelectedPayloadAndRejectsInvalidCardinality()
    {
        var package = Path.Combine(root, "package");
        Directory.CreateDirectory(Path.Combine(package, "fomod"));
        await File.WriteAllTextAsync(Path.Combine(package, "a.tex"), "A", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(package, "b.tex"), "B", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(package, "fomod", "ModuleConfig.xml"), """
        <config>
          <moduleName>Test</moduleName>
          <installSteps>
            <installStep name="Body">
              <optionalFileGroups>
                <group name="Texture" type="SelectExactlyOne">
                  <plugins>
                    <plugin name="A"><files><file source="a.tex" destination="body.tex"/></files><typeDescriptor><type name="Optional"/></typeDescriptor></plugin>
                    <plugin name="B"><files><file source="b.tex" destination="body.tex"/></files><typeDescriptor><type name="Optional"/></typeDescriptor></plugin>
                  </plugins>
                </group>
              </optionalFileGroups>
            </installStep>
          </installSteps>
        </config>
        """, TestContext.Current.CancellationToken);

        var service = new FomodInstallerService(package);
        var game = GameProfile.MonsterHunterWorld(root);
        Assert.Throws<InvalidDataException>(() => service.Plan(new HashSet<string>(), game));
        Assert.Throws<InvalidDataException>(() => service.Plan(new HashSet<string> { "0/0/0", "0/0/1" }, game));

        var plan = service.Plan(new HashSet<string> { "0/0/0" }, game);
        var copy = Assert.Single(plan);
        Assert.Equal("nativePC/body.tex", copy.Destination);
        Assert.EndsWith("a.tex", copy.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RulesEditorRejectsOverlayCyclesOnCurrentSnapshotRepository()
    {
        var db = new ManagerDatabase(Path.Combine(root, "rules.db"));
        await db.InitializeAsync(TestContext.Current.CancellationToken);
        await db.UpsertModAsync(new("a", "a", "a", Path.Combine(root, "a"), true, 1), TestContext.Current.CancellationToken);
        await db.UpsertModAsync(new("b", "b", "b", Path.Combine(root, "b"), true, 2), TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("a", [new("a", @"nativePC\a.tex", new string('a', 64), null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], TestContext.Current.CancellationToken);
        await db.ReplaceModFilesAsync("b", [new("b", @"nativePC\a.tex", new string('b', 64), null, 1, DateTimeOffset.UtcNow, FileClass.Texture)], TestContext.Current.CancellationToken);

        var rules = new RulesEditorService(db);
        await rules.SaveAsync(new("ab", RuleKind.Overlay, RuleScope.ModPair, "a", "b", "b", null, "b overlays a", true, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            rules.SaveAsync(new("ba", RuleKind.Overlay, RuleScope.ModPair, "b", "a", "a", null, "cycle", true, DateTimeOffset.UtcNow)));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowAnalysisUsesCanonicalPlannerDecision()
    {
        var mod = new ModDescriptor("a", "A", "A", root, true, 1);
        var file = new ModFileDescriptor("a", @"nativePC\a.tex", new string('a', 64), null, 1, DateTimeOffset.UtcNow, FileClass.Texture);
        var snapshot = new PlannerSnapshot(
            [mod], [file], [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, DeploymentManifestEntry>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        var row = Assert.Single(WorkflowAnalysis.Explore(snapshot, new DeploymentPlanner(new ConflictEngine())));
        Assert.Equal("a", row.WinnerId);
        Assert.False(row.Blocking);
    }

    [Fact]
    public void AdapterRegistryPreservesGenericSafetyAndMhwSemantics()
    {
        var generic = GameProfile.Generic("example", "Example", root, "Game.exe", "Mods");
        Assert.False(GameAdapters.Resolve(generic).SupportsMhwConflictSemantics);
        Assert.True(GameAdapters.Resolve(GameProfile.MonsterHunterWorld(root)).SupportsMhwConflictSemantics);
    }
}
