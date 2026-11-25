using System.Security.Cryptography;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class InstalledTextureLaunchHealthTests
{
    private static readonly string[] Paths = [@"nativePC\pl\f_equip\pl001_0000\body\tex\e2e.tex", @"nativePC\pl\f_equip\pl001_0000\body\tex\independent-e2e.tex"];

    [Fact]
    public Task ArmorFixtureHeadersPassTheLaunchGateAndTheirOriginalInvalidBytesAreBlocked() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var root = Directory.CreateDirectory(Path.Combine(services.Paths.ModsRoot, "Leather")).FullName;
            byte[][] payloads = [Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray(), [21, 41, 67, 101, 149]];
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, Paths[0]))!);
            const string id = "leather";
            await services.Database.UpsertModAsync(new ModDescriptor(id, "Leather", "Leather", root, true, 1), ct);
            async Task Index(byte[][] bytes)
            {
                var files = new List<ModFileDescriptor>();
                for (var i = 0; i < Paths.Length; i++)
                {
                    File.WriteAllBytes(Path.Combine(root, Paths[i]), bytes[i]);
                    files.Add(new(id, Paths[i], Convert.ToHexStringLower(SHA256.HashData(bytes[i])), null, bytes[i].Length, DateTime.UtcNow, FileClass.Texture));
                }
                await services.Database.ReplaceModFilesAsync(id, files, ct);
            }

            await Index(payloads);
            Assert.False((await services.LaunchGate.EvaluateAsync(ct)).Ready);
            var issues = await services.Health.ScanAsync(ct);
            Assert.Contains(issues, issue => issue.Code == "TEX_BAD_MAGIC");
            Assert.Contains(issues, issue => issue.Code == "TEX_TRUNCATED");

            await Index(payloads.Select(InstalledWpfModLifecycleE2ETests.FixtureTexture).ToArray());
            var result = await services.LaunchGate.EvaluateAsync(ct);
            Assert.True(result.Ready, string.Join(" | ", result.Findings.Select(f => f.Summary)));
        }, DiscoveredGameProfiles.Load().Single(game => game.IsMonsterHunterWorld));

    [Fact]
    public Task LaunchValidatesAppliedTextureWinnerNotShadowedSource() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var relative = Paths[0];
            var invalid = new byte[] { 1, 2, 3 };
            var valid = InstalledWpfModLifecycleE2ETests.FixtureTexture(new byte[4096]);
            foreach (var (id, bytes) in new[] { ("base", invalid), ("overlay", valid) })
            {
                var root = Directory.CreateDirectory(Path.Combine(services.Paths.ToolRoot, id)).FullName;
                var source = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                await File.WriteAllBytesAsync(source, bytes, ct);
                var mod = new ModDescriptor(id, id, id, root, true, id == "base" ? 1 : 2);
                await services.Database.UpsertModAsync(mod, ct);
                Assert.Single(await services.Scanner.CaptureAsync(mod, ct));
            }
            await services.Database.ChainManualFamilyAsync("base", ["base"], [["overlay"]],
                "Texture stack", refuseExistingPairRules: true, ct: ct);
            var plan = services.Planner.Build(await services.PlannerSnapshots.LoadAsync(ct));
            Assert.False(plan.IsBlocked);
            Assert.Equal("overlay", Assert.Single(plan.Changes).ProviderAfter);
            Assert.True((await services.Executor.ApplyAsync(plan, "Apply texture stack", ct: ct)).Success);
            var live = services.Executor.Destination(relative);
            Assert.Equal(valid, await File.ReadAllBytesAsync(live, ct));
            var report = await services.LaunchGate.EvaluateAsync(ct, requireApplied: true);
            Assert.True(report.Ready, string.Join(" | ", report.Findings.Select(f => f.Summary)));
            Assert.Contains(report.Findings, f => f.ModId == "base" &&
                f.Severity == MhwModManager.Automation.AutomationSeverity.Warning &&
                f.Summary.Contains("Not deployed", StringComparison.Ordinal));
            Assert.Contains(await services.Health.ScanAsync(ct), issue =>
                issue.ModId == "base" && issue.Severity == "Error");

            // External tampering is not excused by the saved override rule.
            await File.WriteAllBytesAsync(live, invalid, ct);
            report = await services.LaunchGate.EvaluateAsync(ct, requireApplied: true);
            Assert.False(report.Ready);
            Assert.Contains(report.Findings, f => f.Severity == MhwModManager.Automation.AutomationSeverity.Blocker &&
                f.Summary.Contains("changed outside", StringComparison.OrdinalIgnoreCase));
        }, DiscoveredGameProfiles.Load().Single(game => game.IsMonsterHunterWorld));
}
