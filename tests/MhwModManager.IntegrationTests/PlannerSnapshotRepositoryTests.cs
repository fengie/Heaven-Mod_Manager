using System.Globalization;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class PlannerSnapshotRepositoryTests : IDisposable
{
    private const string SharedPath = @"nativePC\shared.mod3";
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-planner-snapshot-" + Guid.NewGuid().ToString("N"));

    public PlannerSnapshotRepositoryTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    [Fact]
    public async Task Full_snapshot_matches_pre_extraction_query_assembly()
    {
        var db = await CreateSeededAsync("full");
        var expected = await LoadLegacyAsync(db, null, TestToken);
        var actual = await new PlannerSnapshotRepository(db).LoadAsync(TestToken);
        AssertSnapshotEqual(expected, actual);
        Assert.Equal(4, actual.Files.Count);
        Assert.Equal("b", actual.ExactWinners[SharedPath]);
        Assert.Equal("b", actual.ResourceProviders["shared:test"]);
        Assert.Equal("b", actual.CurrentManifest[SharedPath].ProviderModId);
        Assert.Equal("original-shared", actual.Originals[SharedPath]);
    }

    [Fact]
    public async Task Filtered_snapshot_matches_pre_extraction_case_insensitive_distinct_filter()
    {
        var db = await CreateSeededAsync("filtered");
        string[] filter = ["b", "B", "missing"];
        var expected = await LoadLegacyAsync(db, filter, TestToken);
        var actual = await new PlannerSnapshotRepository(db).LoadAsync(filter, TestToken);
        AssertSnapshotEqual(expected, actual);
        Assert.Equal(2, actual.Files.Count);
        Assert.All(actual.Files, file => Assert.Equal("b", file.ModId));
        Assert.Equal(2, actual.Mods.Count);
        Assert.NotEmpty(actual.Rules);
        Assert.NotEmpty(actual.CurrentManifest);
    }

    [Fact]
    public async Task Filtered_snapshot_preserves_legacy_first_casing_sql_semantics()
    {
        var db = await CreateSeededAsync("filtered-casing");
        string[] filter = ["B", "b"];
        var expected = await LoadLegacyAsync(db, filter, TestToken);
        var actual = await new PlannerSnapshotRepository(db).LoadAsync(filter, TestToken);

        AssertSnapshotEqual(expected, actual);
        // Distinct(StringComparer.OrdinalIgnoreCase) keeps the first representative ("B"), while
        // the existing SQLite IN comparison uses default case-sensitive text equality.
        Assert.Empty(actual.Files);
        Assert.Equal(2, actual.Mods.Count);
        Assert.NotEmpty(actual.Rules);
        Assert.NotEmpty(actual.CurrentManifest);
    }

    [Fact]
    public async Task Empty_filter_skips_only_mod_files_and_preserves_other_planner_inputs()
    {
        var db = await CreateSeededAsync("empty");
        var expected = await LoadLegacyAsync(db, Array.Empty<string>(), TestToken);
        var actual = await new PlannerSnapshotRepository(db).LoadAsync(Array.Empty<string>(), TestToken);
        AssertSnapshotEqual(expected, actual);
        Assert.Empty(actual.Files);
        Assert.Equal(2, actual.Mods.Count);
        Assert.NotEmpty(actual.Rules);
        Assert.NotEmpty(actual.ExactWinners);
        Assert.NotEmpty(actual.ResourceProviders);
        Assert.NotEmpty(actual.CurrentManifest);
        Assert.NotEmpty(actual.Originals);
    }

    [Fact]
    public async Task Extracted_snapshot_preserves_representative_planner_output()
    {
        var db = await CreateSeededAsync("planner");
        var planner = new DeploymentPlanner(new ConflictEngine());
        var expected = planner.Build(await LoadLegacyAsync(db, null, TestToken));
        var actual = planner.Build(await new PlannerSnapshotRepository(db).LoadAsync(TestToken));

        Assert.Equal(
            expected.Conflicts.Select(x => (x.Path, x.Kind, x.Blocking, x.WinnerModId, x.ReasonCode, x.RuleId)).ToArray(),
            actual.Conflicts.Select(x => (x.Path, x.Kind, x.Blocking, x.WinnerModId, x.ReasonCode, x.RuleId)).ToArray());
        Assert.Equal(
            expected.Changes.Select(x => (x.Sequence, x.Kind, x.Path, x.BeforeBlobSha256, x.AfterBlobSha256, x.ProviderBefore, x.ProviderAfter, x.RuleId)).ToArray(),
            actual.Changes.Select(x => (x.Sequence, x.Kind, x.Path, x.BeforeBlobSha256, x.AfterBlobSha256, x.ProviderBefore, x.ProviderAfter, x.RuleId)).ToArray());
        Assert.Equal(expected.Preconditions.ToArray(), actual.Preconditions.ToArray());

        var shared = Assert.Single(actual.Conflicts, x => PathRules.Comparer.Equals(x.Path, SharedPath));
        Assert.False(shared.Blocking);
        Assert.Equal("b", shared.WinnerModId);
        Assert.Equal("exact-file-winner", shared.ReasonCode);
        Assert.Null(shared.RuleId); // ExactWinners preserves path -> winner, not the originating rule ID.
    }

    [Fact]
    public async Task Already_canceled_token_remains_canceled()
    {
        var db = await CreateSeededAsync("canceled");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PlannerSnapshotRepository(db).LoadAsync(cts.Token));
    }

    private async Task<ManagerDatabase> CreateSeededAsync(string name)
    {
        var db = new ManagerDatabase(Path.Combine(root, name, "manager.db"));
        await db.InitializeAsync(TestToken);
        var now = DateTimeOffset.Parse("2026-09-27T12:00:00Z", CultureInfo.InvariantCulture);

        await db.UpsertModAsync(new("a", "A", "Alpha", Path.Combine(root, name, "a"), true, 10), TestToken);
        await db.UpsertModAsync(new("b", "B", "Beta", Path.Combine(root, name, "b"), true, 20), TestToken);
        await db.ReplaceModFilesAsync("a",
        [
            new("a", SharedPath, "a-shared", "fa", 10, now, FileClass.Structural),
            new("a", @"nativePC\a-only.tex", "a-only", null, 11, now.AddMinutes(1), FileClass.Texture)
        ], TestToken);
        await db.ReplaceModFilesAsync("b",
        [
            new("b", SharedPath, "b-shared", "fb", 12, now.AddMinutes(2), FileClass.Structural),
            new("b", @"nativePC\b-only.dll", "b-only", null, 13, now.AddMinutes(3), FileClass.Plugin)
        ], TestToken);

        await db.ExecuteAsync(
            """
            INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
            VALUES('exact-b','ExactWinner','ExactPath',NULL,NULL,'b',$p,'test exact winner',1,$t)
            """,
            new Dictionary<string, object?> { ["$p"] = SharedPath, ["$t"] = now.ToString("O", CultureInfo.InvariantCulture) },
            TestToken);
        await db.ExecuteAsync("INSERT INTO resource_providers(namespace,mod_id) VALUES('shared:test','b')", ct: TestToken);
        await db.ExecuteAsync(
            """
            INSERT INTO deployment_manifest(path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at)
            VALUES($p,'b','b-shared','b-shared','exact-b',$t)
            """,
            new Dictionary<string, object?> { ["$p"] = SharedPath, ["$t"] = now.ToString("O", CultureInfo.InvariantCulture) },
            TestToken);
        await db.ExecuteAsync(
            "INSERT INTO original_files(path,blob_sha256,captured_at) VALUES($p,'original-shared',$t)",
            new Dictionary<string, object?> { ["$p"] = SharedPath, ["$t"] = now.ToString("O", CultureInfo.InvariantCulture) },
            TestToken);
        return db;
    }

    private static async Task<PlannerSnapshot> LoadLegacyAsync(ManagerDatabase db, IReadOnlyCollection<string>? fileModIds, CancellationToken ct)
    {
        var mods = await db.GetModsAsync(ct);
        var files = new List<ModFileDescriptor>();
        var rules = new List<ConflictRule>();
        var exact = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var resources = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new Dictionary<string,DeploymentManifestEntry>(StringComparer.OrdinalIgnoreCase);
        var originals = new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        await using var c = await db.OpenAsync(ct);

        async Task ReadFiles()
        {
            if (fileModIds is { Count: 0 }) return;
            await using var cmd = c.CreateCommand();
            if (fileModIds is null)
            {
                cmd.CommandText = "SELECT mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files";
            }
            else
            {
                var ids = fileModIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var names = new string[ids.Length];
                for (var i=0;i<ids.Length;i++)
                {
                    names[i] = "$m" + i.ToString(CultureInfo.InvariantCulture);
                    cmd.Parameters.AddWithValue(names[i], ids[i]);
                }
                cmd.CommandText = $"SELECT mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files WHERE mod_id IN ({string.Join(',', names)})";
            }
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                files.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetInt64(4),DateTimeOffset.Parse(r.GetString(5),CultureInfo.InvariantCulture),Enum.Parse<FileClass>(r.GetString(6))));
        }

        async Task ReadRules()
        {
            await using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at FROM conflict_rules";
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))
            {
                var rule=new ConflictRule(r.GetString(0),Enum.Parse<RuleKind>(r.GetString(1)),Enum.Parse<RuleScope>(r.GetString(2)),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.GetString(7),r.GetInt64(8)!=0,DateTimeOffset.Parse(r.GetString(9),CultureInfo.InvariantCulture));
                rules.Add(rule);
                if(rule.Kind==RuleKind.ExactWinner&&rule.PathPattern is not null&&rule.WinnerModId is not null) exact[rule.PathPattern]=rule.WinnerModId;
            }
        }

        async Task ReadResource(){await using var cmd=c.CreateCommand();cmd.CommandText="SELECT namespace,mod_id FROM resource_providers";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))resources[r.GetString(0)]=r.GetString(1);}
        async Task ReadManifest(){await using var cmd=c.CreateCommand();cmd.CommandText="SELECT path,provider_mod_id,blob_sha256,expected_live_sha256,rule_id,deployed_at FROM deployment_manifest";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var e=new DeploymentManifestEntry(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),DateTimeOffset.Parse(r.GetString(5),CultureInfo.InvariantCulture));manifest[e.Path]=e;}}
        async Task ReadOriginals(){await using var cmd=c.CreateCommand();cmd.CommandText="SELECT path,blob_sha256 FROM original_files";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))originals[r.GetString(0)]=r.IsDBNull(1)?null:r.GetString(1);}

        await ReadFiles();await ReadRules();await ReadResource();await ReadManifest();await ReadOriginals();
        return new(mods,files,rules,exact,resources,manifest,originals);
    }

    private static void AssertSnapshotEqual(PlannerSnapshot expected, PlannerSnapshot actual)
    {
        Assert.Equal(expected.Mods.ToArray(), actual.Mods.ToArray());
        Assert.Equal(expected.Files.ToArray(), actual.Files.ToArray());
        Assert.Equal(expected.Rules.ToArray(), actual.Rules.ToArray());
        Assert.Equal(expected.ExactWinners.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray(), actual.ExactWinners.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(expected.ResourceProviders.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray(), actual.ResourceProviders.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(expected.CurrentManifest.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray(), actual.CurrentManifest.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(expected.Originals.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray(), actual.Originals.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
