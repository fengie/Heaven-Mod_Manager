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
}
