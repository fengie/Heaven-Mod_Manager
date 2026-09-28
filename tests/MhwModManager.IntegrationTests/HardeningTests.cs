using System.Diagnostics;
using System.IO.Compression;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class HardeningTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-v81-hardening-" + Guid.NewGuid().ToString("N"));
    public HardeningTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    private async Task<(string game, ManagerDatabase db, HashingService hashing, BlobStore blobs)> CreateAsync(
        string name,
        IAtomicReplaceBackend? atomicReplaceBackend = null)
    {
        var game = Path.Combine(root, name, "game");
        var state = Path.Combine(root, name, "state");
        Directory.CreateDirectory(Path.Combine(game, "nativePC"));
        var db = new ManagerDatabase(Path.Combine(state, "manager.db"));
        await db.InitializeAsync(TestToken);
        var hashing = new HashingService();
        var blobs = new BlobStore(Path.Combine(state, "blobs"), db, atomicReplaceBackend);
        return (game, db, hashing, blobs);
    }

    private async Task<string> BlobAsync(BlobStore blobs, string name, string contents)
    {
        var file = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, contents, TestToken);
        return await blobs.CaptureAsync(file, TestToken);
    }

    private sealed class ScriptedReplaceBackend(int nativeErrorCode, Action<string, string> materializeFailure) : IAtomicReplaceBackend
    {
        public string? LastReplacementPath { get; private set; }

        public bool TryReplace(string replaced, string replacement, string? backup, out int errorCode)
        {
            LastReplacementPath = replacement;
            materializeFailure(replaced, replacement);
            errorCode = nativeErrorCode;
            return false;
        }
    }

    private static async Task<string?> ReadOperationStateAsync(ManagerDatabase db, string operationId)
    {
        await using var connection = await db.OpenAsync(TestToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM operations WHERE id=$id";
        command.Parameters.AddWithValue("$id", operationId);
        return (string?)await command.ExecuteScalarAsync(TestToken);
    }

    private static async Task<string?> ReadJournalStatusAsync(ManagerDatabase db, string operationId, int sequence)
    {
        await using var connection = await db.OpenAsync(TestToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM operation_journal WHERE operation_id=$id AND seq=$sequence";
        command.Parameters.AddWithValue("$id", operationId);
        command.Parameters.AddWithValue("$sequence", sequence);
        return (string?)await command.ExecuteScalarAsync(TestToken);
    }

    private static void CreateDirectoryJunction(string link, string target)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        var info=new ProcessStartInfo("cmd.exe",$"/d /c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute=false,
            RedirectStandardOutput=true,
            RedirectStandardError=true,
            CreateNoWindow=true
        };
        using var process=Process.Start(info)??throw new InvalidOperationException("Could not start cmd.exe to create junction.");
        process.WaitForExit();
        if(process.ExitCode!=0)
        {
            var error=process.StandardError.ReadToEnd();
            var output=process.StandardOutput.ReadToEnd();
            throw new IOException($"Could not create test junction '{link}' -> '{target}'. Exit={process.ExitCode}; stdout={output}; stderr={error}");
        }
        Assert.True((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0);
    }

    [Theory]
    [InlineData("after-journal")]
    [InlineData("before-first-mutation")]
    [InlineData("after-file-write")]
    [InlineData("after-files-written")]
    [InlineData("before-db-commit")]
    public async Task Crash_before_commit_recovers_to_exact_before_image(string stage)
    {
        var (game, db, hashing, blobs) = await CreateAsync("crash-" + stage);
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var after = await BlobAsync(blobs, stage, "MOD");
        var plan = new DeploymentPlan("op-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, after, null, "m", null, null)], [], []);

        var crashing = new DeploymentExecutor(db, blobs, hashing, game, (at, _) =>
        {
            if (at == stage) throw new SimulatedCrashException(stage);
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.ApplyAsync(plan, "fixture", ct: TestToken));

        var recovered = new DeploymentExecutor(db, blobs, hashing, game);
        await recovered.RecoverIncompleteAsync(TestToken);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        var snap = await new PlannerSnapshotRepository(db).LoadAsync(TestToken);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.CurrentManifest.Keys);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.Originals.Keys);
    }

    [Theory]
    [InlineData("after-db-commit")]
    [InlineData("before-cleanup")]
    public async Task Crash_after_durable_commit_stays_committed(string stage)
    {
        var (game, db, hashing, blobs) = await CreateAsync("commit-" + stage);
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var after = await BlobAsync(blobs, stage, "MOD");
        var plan = new DeploymentPlan("op-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, after, null, "m", null, null)], [], []);

        var crashing = new DeploymentExecutor(db, blobs, hashing, game, (at, _) =>
        {
            if (at == stage) throw new SimulatedCrashException(stage);
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.ApplyAsync(plan, "fixture", ct: TestToken));

        var recovered = new DeploymentExecutor(db, blobs, hashing, game);
        await recovered.RecoverIncompleteAsync(TestToken);
        Assert.Equal("MOD", await File.ReadAllTextAsync(live, TestToken));
        var snap = await new PlannerSnapshotRepository(db).LoadAsync(TestToken);
        Assert.Equal("m", snap.CurrentManifest[@"nativePC\x.tex"].ProviderModId);
        Assert.Contains(@"nativePC\x.tex", snap.Originals.Keys);
    }

    [Fact]
    public async Task Whole_plan_preflight_prevents_partial_write_when_later_path_is_stale()
    {
        var (game, db, hashing, blobs) = await CreateAsync("preflight");
        var a = Path.Combine(game, "nativePC", "a.tex");
        var b = Path.Combine(game, "nativePC", "b.tex");
        await File.WriteAllTextAsync(a, "A0", TestToken);
        await File.WriteAllTextAsync(b, "B0", TestToken);
        var a0 = await blobs.CaptureAsync(a, TestToken);
        var b0 = await blobs.CaptureAsync(b, TestToken);
        var a1 = await BlobAsync(blobs, "a1", "A1");
        var b1 = await BlobAsync(blobs, "b1", "B1");
        await File.WriteAllTextAsync(b, "EXTERNAL", TestToken);

        var plan = new DeploymentPlan("stale-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
        [
            new(1, ChangeKind.Replace, @"nativePC\a.tex", a0, a1, "old", "new", a0, null),
            new(2, ChangeKind.Replace, @"nativePC\b.tex", b0, b1, "old", "new", b0, null)
        ], [], []);

        var result = await new DeploymentExecutor(db, blobs, hashing, game).ApplyAsync(plan, "preflight", ct: TestToken);
        Assert.False(result.Success);
        Assert.Equal("A0", await File.ReadAllTextAsync(a, TestToken));
        Assert.Equal("EXTERNAL", await File.ReadAllTextAsync(b, TestToken));
        Assert.Equal(0, result.FilesChanged);
    }

    [Theory]
    [InlineData(ChangeKind.Add)]
    [InlineData(ChangeKind.Replace)]
    [InlineData(ChangeKind.Remove)]
    public async Task Deployment_mutations_reject_parent_junction_outside_game_root(ChangeKind kind)
    {
        if(!OperatingSystem.IsWindows())return;

        var (game, db, hashing, blobs) = await CreateAsync("reparse-" + kind);
        var external=Path.Combine(root,"external-" + kind);
        Directory.CreateDirectory(external);
        var sentinel=Path.Combine(external,"sentinel.txt");
        await File.WriteAllTextAsync(sentinel,"SENTINEL",TestToken);

        var redirect=Path.Combine(game,"nativePC","redirect");
        CreateDirectoryJunction(redirect,external);

        var after=await BlobAsync(blobs,"reparse-after-" + kind,"AFTER");
        string key;
        DeploymentChange change;
        if(kind==ChangeKind.Add)
        {
            key=@"nativePC\redirect\new.bin";
            change=new(1,kind,key,null,after,null,"m",null,null);
        }
        else
        {
            key=@"nativePC\redirect\victim.bin";
            var victim=Path.Combine(external,"victim.bin");
            await File.WriteAllTextAsync(victim,"OUTSIDE",TestToken);
            var before=await blobs.CaptureAsync(victim,TestToken);
            change=kind==ChangeKind.Replace
                ? new(1,kind,key,before,after,"old","new",before,null)
                : new(1,kind,key,before,null,"old",null,before,null);
        }

        var planId="reparse-" + kind + "-" + Guid.NewGuid().ToString("N");
        var plan=new DeploymentPlan(planId,DateTimeOffset.UtcNow,[change],[],[]);
        var executor=new DeploymentExecutor(db,blobs,hashing,game);

        var ex=await Assert.ThrowsAsync<IOException>(()=>executor.ApplyAsync(plan,"reparse containment fixture",ct:TestToken));
        Assert.Contains("reparse point",ex.Message,StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SENTINEL",await File.ReadAllTextAsync(sentinel,TestToken));
        if(kind==ChangeKind.Add)Assert.False(File.Exists(Path.Combine(external,"new.bin")));
        else
        {
            var victim=Path.Combine(external,"victim.bin");
            Assert.True(File.Exists(victim));
            Assert.Equal("OUTSIDE",await File.ReadAllTextAsync(victim,TestToken));
        }

        var snapshot=await new PlannerSnapshotRepository(db).LoadAsync(TestToken);
        Assert.DoesNotContain(key,snapshot.CurrentManifest.Keys);
        await using var connection=await db.OpenAsync(TestToken);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT COUNT(*) FROM operations WHERE id=$id";
        command.Parameters.AddWithValue("$id",planId);
        Assert.Equal(0L,(long)(await command.ExecuteScalarAsync(TestToken))!);
    }

    [Fact]
    public async Task Startup_recovery_refuses_parent_junction_introduced_while_app_was_down()
    {
        if(!OperatingSystem.IsWindows())return;

        var (game, db, hashing, blobs) = await CreateAsync("reparse-recovery");
        var managed=Path.Combine(game,"nativePC","managed");
        Directory.CreateDirectory(managed);
        var live=Path.Combine(managed,"x.bin");
        await File.WriteAllTextAsync(live,"BEFORE",TestToken);
        var after=await BlobAsync(blobs,"reparse-recovery-after","AFTER");
        var planId="reparse-recovery-" + Guid.NewGuid().ToString("N");
        var plan=new DeploymentPlan(planId,DateTimeOffset.UtcNow,
            [new(1,ChangeKind.Add,@"nativePC\managed\x.bin",null,after,null,"m",null,null)],[],[]);

        var crashing=new DeploymentExecutor(db,blobs,hashing,game,(stage,sequence)=>
        {
            if(stage=="after-file-write"&&sequence==1)throw new SimulatedCrashException("crash after live write");
        });
        await Assert.ThrowsAsync<SimulatedCrashException>(()=>crashing.ApplyAsync(plan,"reparse recovery fixture",ct:TestToken));
        Assert.Equal("AFTER",await File.ReadAllTextAsync(live,TestToken));

        Directory.Delete(managed,true);
        var external=Path.Combine(root,"reparse-recovery-external");
        Directory.CreateDirectory(external);
        var outside=Path.Combine(external,"x.bin");
        await File.WriteAllTextAsync(outside,"OUTSIDE",TestToken);
        CreateDirectoryJunction(managed,external);

        var recovering=new DeploymentExecutor(db,blobs,hashing,game);
        var ex=await Assert.ThrowsAsync<IOException>(()=>recovering.RecoverIncompleteAsync(TestToken));
        Assert.Contains("Physical containment failed",ex.Message,StringComparison.OrdinalIgnoreCase);
        Assert.Equal("OUTSIDE",await File.ReadAllTextAsync(outside,TestToken));

        await using var connection=await db.OpenAsync(TestToken);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT state FROM operations WHERE id=$id";
        command.Parameters.AddWithValue("$id",planId);
        Assert.Equal(OperationState.RecoveryRequired.ToString(),(string?)await command.ExecuteScalarAsync(TestToken));
    }

    [Fact]
    public async Task Atomic_replace_1175_failure_preserves_recoverable_before_image()
    {
        if(!OperatingSystem.IsWindows())return;

        var backend=new ScriptedReplaceBackend(1175,(_,_)=>{});
        var (game,db,hashing,blobs)=await CreateAsync("replace-1175",backend);
        var live=Path.Combine(game,"nativePC","replace.bin");
        await File.WriteAllTextAsync(live,"BEFORE",TestToken);
        var before=await blobs.CaptureAsync(live,TestToken);
        var after=await BlobAsync(blobs,"replace-1175-after","AFTER");
        var planId="replace-1175-" + Guid.NewGuid().ToString("N");
        var plan=new DeploymentPlan(planId,DateTimeOffset.UtcNow,
            [new(1,ChangeKind.Replace,@"nativePC\replace.bin",before,after,null,"m",before,null)],[],[]);

        var result=await new DeploymentExecutor(db,blobs,hashing,game).ApplyAsync(plan,"replace 1175 fixture",ct:TestToken);

        Assert.False(result.Success);
        Assert.True(result.RollbackCompleted);
        Assert.Equal("BEFORE",await File.ReadAllTextAsync(live,TestToken));
        Assert.Equal(OperationState.RolledBack.ToString(),await ReadOperationStateAsync(db,planId));
        Assert.Equal("RolledBack",await ReadJournalStatusAsync(db,planId,1));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(live)!, ".*.mhwmm.tmp"));
    }

    [Fact]
    public async Task Atomic_replace_1176_failure_preserves_staged_replacement_for_recovery()
    {
        if(!OperatingSystem.IsWindows())return;

        var backend=new ScriptedReplaceBackend(1176,(replaced,_)=>File.Delete(replaced));
        var (game,db,hashing,blobs)=await CreateAsync("replace-1176",backend);
        var live=Path.Combine(game,"nativePC","replace.bin");
        await File.WriteAllTextAsync(live,"BEFORE",TestToken);
        var before=await blobs.CaptureAsync(live,TestToken);
        var after=await BlobAsync(blobs,"replace-1176-after","AFTER");
        var planId="replace-1176-" + Guid.NewGuid().ToString("N");
        var plan=new DeploymentPlan(planId,DateTimeOffset.UtcNow,
            [new(1,ChangeKind.Replace,@"nativePC\replace.bin",before,after,null,"m",before,null)],[],[]);

        var result=await new DeploymentExecutor(db,blobs,hashing,game).ApplyAsync(plan,"replace 1176 fixture",ct:TestToken);

        Assert.False(result.Success);
        Assert.False(result.RollbackCompleted);
        Assert.Equal(FailureCategory.DataIntegrityFailure,result.FailureCategory);
        Assert.False(File.Exists(live));
        var replacement=Assert.IsType<string>(backend.LastReplacementPath);
        Assert.True(File.Exists(replacement));
        Assert.Equal("AFTER",await File.ReadAllTextAsync(replacement,TestToken));
        Assert.Equal(OperationState.RecoveryRequired.ToString(),await ReadOperationStateAsync(db,planId));
        Assert.Equal("Writing",await ReadJournalStatusAsync(db,planId,1));
    }

    [Fact]
    public async Task Atomic_replace_1177_failure_preserves_both_documented_recovery_images()
    {
        if(!OperatingSystem.IsWindows())return;

        string? displaced=null;
        var backend=new ScriptedReplaceBackend(1177,(replaced,_)=>
        {
            displaced=replaced + ".1177-displaced";
            File.Move(replaced,displaced,false);
        });
        var (game,db,hashing,blobs)=await CreateAsync("replace-1177",backend);
        var live=Path.Combine(game,"nativePC","replace.bin");
        await File.WriteAllTextAsync(live,"BEFORE",TestToken);
        var before=await blobs.CaptureAsync(live,TestToken);
        var after=await BlobAsync(blobs,"replace-1177-after","AFTER");
        var planId="replace-1177-" + Guid.NewGuid().ToString("N");
        var plan=new DeploymentPlan(planId,DateTimeOffset.UtcNow,
            [new(1,ChangeKind.Replace,@"nativePC\replace.bin",before,after,null,"m",before,null)],[],[]);

        var result=await new DeploymentExecutor(db,blobs,hashing,game).ApplyAsync(plan,"replace 1177 fixture",ct:TestToken);

        Assert.False(result.Success);
        Assert.False(result.RollbackCompleted);
        Assert.Equal(FailureCategory.DataIntegrityFailure,result.FailureCategory);
        Assert.False(File.Exists(live));
        var replacement=Assert.IsType<string>(backend.LastReplacementPath);
        Assert.True(File.Exists(replacement));
        Assert.Equal("AFTER",await File.ReadAllTextAsync(replacement,TestToken));
        Assert.NotNull(displaced);
        Assert.True(File.Exists(displaced));
        Assert.Equal("BEFORE",await File.ReadAllTextAsync(displaced!,TestToken));
        Assert.Equal(OperationState.RecoveryRequired.ToString(),await ReadOperationStateAsync(db,planId));
        Assert.Equal("Writing",await ReadJournalStatusAsync(db,planId,1));
    }

    [Fact]
    public async Task Scanner_detects_same_size_same_timestamp_source_edit()
    {
        var (_, db, hashing, blobs) = await CreateAsync("scanner");
        var source = Path.Combine(root, "scanner", "mod");
        Directory.CreateDirectory(Path.Combine(source, "nativePC"));
        var file = Path.Combine(source, "nativePC", "x.tex");
        await File.WriteAllTextAsync(file, "AAAA", TestToken);
        var timestamp = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(file, timestamp);
        var mod = new ModDescriptor("m", "M", "M", source, true, 0);
        await db.UpsertModAsync(mod, TestToken);
        var scanner = new ModScanner(db, blobs, hashing);
        var first = await scanner.CaptureAsync(mod, TestToken);

        await File.WriteAllTextAsync(file, "BBBB", TestToken);
        File.SetLastWriteTimeUtc(file, timestamp);
        var second = await scanner.CaptureAsync(mod, TestToken);

        Assert.NotEqual(first.Single().BlobSha256, second.Single().BlobSha256);
    }

    [Fact]
    public async Task Archive_extraction_rejects_parent_traversal()
    {
        var zip = Path.Combine(root, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("bad".AsMemory(), TestToken);
        }

        var destination = Path.Combine(root, "extract");
        var inspector = new ArchiveInspector();
        var inspection = await inspector.InspectAsync(zip, TestToken);
        Assert.True(inspection.HasSuspiciousPaths);
        Assert.Throws<InvalidDataException>(() => inspector.ExtractSafely(zip, destination, root, TestToken));
        Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
    }

    [Fact]
    public async Task Archive_extraction_rejects_junction_ancestor_above_destination()
    {
        if(!OperatingSystem.IsWindows())return;
        var workspace=Path.Combine(root,"archive-anchor");
        var external=Path.Combine(root,"archive-anchor-target");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(external);
        var modsRoot=Path.Combine(workspace,"Mods");
        CreateDirectoryJunction(modsRoot,external);

        var zip=Path.Combine(workspace,"fixture.zip");
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
        {
            var entry=archive.CreateEntry("nativePC/x.tex");
            await using var writer=new StreamWriter(entry.Open());
            await writer.WriteAsync("payload".AsMemory(),TestToken);
        }

        var destination=Path.Combine(modsRoot,"fixture.importing");
        var inspector=new ArchiveInspector();
        Assert.Throws<InvalidDataException>(()=>inspector.ExtractSafely(zip,destination,modsRoot,TestToken));
        Assert.False(File.Exists(Path.Combine(external,"fixture.importing","nativePC","x.tex")));
    }

    [Fact]
    public async Task Sqlite_integrity_and_wal_survive_parallel_reads()
    {
        var (_, db, _, _) = await CreateAsync("db");
        await db.UpsertModAsync(new ModDescriptor("m", "M", "M", "source", false, 0), TestToken);
        var reads = Enumerable.Range(0, 24).Select(_ => db.GetModsAsync(TestToken)).ToArray();
        var results = await Task.WhenAll(reads);
        Assert.All(results, mods => Assert.Single(mods));
        Assert.Equal("ok", await db.IntegrityCheckAsync(TestToken));
    }
    [Fact]
    public async Task Interrupted_rollback_enters_recovery_required_then_restart_finishes()
    {
        var (game, db, hashing, blobs) = await CreateAsync("rollback-interrupt");
        var live = Path.Combine(game, "nativePC", "x.tex");
        await File.WriteAllTextAsync(live, "ORIGINAL", TestToken);
        var after = await BlobAsync(blobs, "rollback-mod", "MOD");
        var plan = new DeploymentPlan("rollback-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            [new(1, ChangeKind.Add, @"nativePC\x.tex", null, after, null, "m", null, null)], [], []);

        var executor = new DeploymentExecutor(db, blobs, hashing, game, (stage, sequence) =>
        {
            if(stage == "after-file-write" && sequence == 1) throw new IOException("force normal failure");
            if(stage == "during-rollback") throw new SimulatedCrashException("rollback interrupted");
        });
        var failed = await executor.ApplyAsync(plan, "rollback fixture", ct: TestToken);
        Assert.False(failed.Success);
        Assert.False(failed.RollbackCompleted);
        Assert.Equal(FailureCategory.DataIntegrityFailure, failed.FailureCategory);

        await new DeploymentExecutor(db, blobs, hashing, game).RecoverIncompleteAsync(TestToken);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(live, TestToken));
        var snap = await new PlannerSnapshotRepository(db).LoadAsync(TestToken);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.CurrentManifest.Keys);
        Assert.DoesNotContain(@"nativePC\x.tex", snap.Originals.Keys);
    }

    [Fact]
    public async Task Adopted_manual_files_are_not_offered_again_until_the_live_bytes_change()
    {
        var (game, db, hashing, _) = await CreateAsync("adoption");
        var modsRoot=Path.Combine(root,"adoption","Mods");Directory.CreateDirectory(modsRoot);
        var live=Path.Combine(game,"nativePC","manual.tex");
        await File.WriteAllTextAsync(live,"MANUAL-A",TestToken);
        var adoption=new UnmanagedAdoptionService(db,new PlannerSnapshotRepository(db),hashing,modsRoot,GameProfile.MonsterHunterWorld(game));

        Assert.Equal(1,await adoption.CountAsync(TestToken));
        var result=await adoption.AdoptAsync(TestToken);
        Assert.True(result.Created);
        Assert.Equal(1,result.FileCount);
        Assert.NotNull(result.SourceFolder);
        Assert.True(File.Exists(Path.Combine(result.SourceFolder!,"nativePC","manual.tex")));
        Assert.Equal(0,await adoption.CountAsync(TestToken));

        await File.WriteAllTextAsync(live,"MANUAL-B",TestToken);
        Assert.Equal(1,await adoption.CountAsync(TestToken));
    }

    [Fact]
    public async Task Manual_family_chain_persists_roles_and_turns_self_conflict_into_explicit_optional_overlay()
    {
        var (_,db,_,_)=await CreateAsync("manual-family-chain");
        var main=new ModDescriptor("main","Generic Armor","Generic Armor","main",true,10);
        var optional=new ModDescriptor("optional","Unrecognized Extra Piece","Unrecognized Extra Piece","optional",true,20);
        await db.UpsertModAsync(main,TestToken);await db.UpsertModAsync(optional,TestToken);
        var now=DateTimeOffset.UtcNow;
        var path=@"nativePC\pl\f_equip\pl999_0000\f_body999_0000.mod3";
        await db.ReplaceModFilesAsync(main.Id,[new(main.Id,path,"aa",null,10,now,FileClass.Structural)],TestToken);
        await db.ReplaceModFilesAsync(optional.Id,[new(optional.Id,path,"bb",null,10,now,FileClass.Structural)],TestToken);

        var planner=new DeploymentPlanner(new ConflictEngine());
        var before=planner.Build(await new PlannerSnapshotRepository(db).LoadAsync(TestToken));
        Assert.True(before.IsBlocked);

        var familyId=await db.ChainManualFamilyAsync(main.Id,[main.Id],[[optional.Id]],main.DisplayName,TestToken);
        var mods=(await db.GetModsAsync(TestToken)).ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
        Assert.Equal(familyId,mods[main.Id].FamilyId);Assert.Equal(familyId,mods[optional.Id].FamilyId);
        Assert.Equal("Main",mods[main.Id].FamilyRole);Assert.Equal("Optional",mods[optional.Id].FamilyRole);

        var after=planner.Build(await new PlannerSnapshotRepository(db).LoadAsync(TestToken));
        Assert.False(after.IsBlocked);
        var decision=Assert.Single(after.Conflicts,x=>PathRules.Comparer.Equals(x.Path,path));
        Assert.Equal(ConflictKind.UserOverlayRule,decision.Kind);
        Assert.Equal(optional.Id,decision.WinnerModId);
        Assert.Equal(Confidence.Explicit,decision.Confidence);
    }

    [Fact]
    public async Task Manual_family_chain_replaces_old_pair_incompatibility_for_the_groups_user_explicitly_chains()
    {
        var (_,db,_,_)=await CreateAsync("manual-family-overrides-old-pair");
        var main=new ModDescriptor("main-old","Root","Root","root",true,10,FamilyId:"legacy-family");
        var optional=new ModDescriptor("opt-old","Extra","Extra","extra",true,20,FamilyId:"legacy-family");
        await db.UpsertModAsync(main,TestToken);await db.UpsertModAsync(optional,TestToken);
        var now=DateTimeOffset.UtcNow;var path=@"nativePC\pl\f_equip\pl997_0000\shared.mod3";
        await db.ReplaceModFilesAsync(main.Id,[new(main.Id,path,"ma",null,10,now,FileClass.Structural)],TestToken);
        await db.ReplaceModFilesAsync(optional.Id,[new(optional.Id,path,"op",null,10,now,FileClass.Structural)],TestToken);
        await db.ExecuteAsync("""
            INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
            VALUES('old-incompatible','Incompatible','ModPair',$a,$b,NULL,NULL,'old user decision',1,$t)
            """,new Dictionary<string,object?>{{"$a",main.Id},{"$b",optional.Id},{"$t",now.ToString("O",System.Globalization.CultureInfo.InvariantCulture)}},TestToken);

        var planner=new DeploymentPlanner(new ConflictEngine());
        Assert.True(planner.Build(await new PlannerSnapshotRepository(db).LoadAsync(TestToken)).IsBlocked);
        await db.ChainManualFamilyAsync(main.Id,[main.Id],[[optional.Id]],main.DisplayName,TestToken);
        var after=planner.Build(await new PlannerSnapshotRepository(db).LoadAsync(TestToken));
        Assert.False(after.IsBlocked);
        var decision=Assert.Single(after.Conflicts,x=>PathRules.Comparer.Equals(x.Path,path));
        Assert.Equal(ConflictKind.UserOverlayRule,decision.Kind);
        Assert.Equal(optional.Id,decision.WinnerModId);
    }

    [Fact]
    public async Task Manual_family_chain_orders_multiple_optional_groups_so_shared_base_files_never_self_conflict()
    {
        var (_,db,_,_)=await CreateAsync("manual-family-multi");
        var main=new ModDescriptor("main3","Armor Root","Armor Root","main3",true,10);
        var chest=new ModDescriptor("chest3","Chest Option","Chest Option","chest3",true,20);
        var waist=new ModDescriptor("waist3","Waist Option","Waist Option","waist3",true,30);
        foreach(var mod in new[]{main,chest,waist})await db.UpsertModAsync(mod,TestToken);
        var now=DateTimeOffset.UtcNow;var path=@"nativePC\pl\f_equip\pl998_0000\shared.mod3";
        await db.ReplaceModFilesAsync(main.Id,[new(main.Id,path,"m1",null,10,now,FileClass.Structural)],TestToken);
        await db.ReplaceModFilesAsync(chest.Id,[new(chest.Id,path,"c1",null,10,now,FileClass.Structural)],TestToken);
        await db.ReplaceModFilesAsync(waist.Id,[new(waist.Id,path,"w1",null,10,now,FileClass.Structural)],TestToken);

        await db.ChainManualFamilyAsync(main.Id,[main.Id],[[chest.Id],[waist.Id]],main.DisplayName,TestToken);
        var plan=new DeploymentPlanner(new ConflictEngine()).Build(await new PlannerSnapshotRepository(db).LoadAsync(TestToken));
        Assert.False(plan.IsBlocked);
        var decision=Assert.Single(plan.Conflicts,x=>PathRules.Comparer.Equals(x.Path,path));
        Assert.Equal(waist.Id,decision.WinnerModId);
        Assert.Equal(ConflictKind.UserOverlayRule,decision.Kind);
    }

    [Fact]
    public async Task Game_build_change_marks_binary_mods_but_not_texture_only_mods_for_revalidation()
    {
        var (game, db, _, _) = await CreateAsync("game-build");
        var exe=Path.Combine(game,"MonsterHunterWorld.exe");
        await File.WriteAllBytesAsync(exe,[1,2,3,4],TestToken);
        var binary=new ModDescriptor("binary","Plugin Mod","Plugin Mod","binary",true,0);
        var texture=new ModDescriptor("texture","Texture Mod","Texture Mod","texture",true,0);
        await db.UpsertModAsync(binary,TestToken);await db.UpsertModAsync(texture,TestToken);
        var now=DateTimeOffset.UtcNow;
        await db.ReplaceModFilesAsync(binary.Id,[new(binary.Id,@"root\loader.dll","aa",null,4,now,FileClass.Plugin)],TestToken);
        await db.ReplaceModFilesAsync(texture.Id,[new(texture.Id,@"nativePC\pl\f_equip\skin.tex","bb",null,4,now,FileClass.Texture)],TestToken);
        var monitor=new GameBuildMonitor(db,new PlannerSnapshotRepository(db),GameProfile.MonsterHunterWorld(game));
        var baseline=await monitor.CheckAsync(TestToken);Assert.False(baseline.Changed);

        await File.WriteAllBytesAsync(exe,[5,6,7,8],TestToken);
        var changed=await monitor.CheckAsync(TestToken);Assert.True(changed.Changed);Assert.Equal(1,changed.MarkedMods);
        var mods=(await db.GetModsAsync(TestToken)).ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
        Assert.True(mods["binary"].NeedsRevalidation);
        Assert.False(mods["texture"].NeedsRevalidation);
    }

}
