using System.Text.Json;
using MhwModManager.App;
using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class MultiGameTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private static CancellationToken TestToken=>TestContext.Current.CancellationToken;
    private readonly string root=Path.Combine(Path.GetTempPath(),"umm-multigame-"+Guid.NewGuid().ToString("N"));
    public MultiGameTests()=>Directory.CreateDirectory(root);
    public void Dispose(){try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}

    [Fact]
    public async Task Games_page_first_refresh_discovers_MHW_and_other_installed_games_once()
    {
        var mhw=Path.Combine(root,"steam-mhw");Directory.CreateDirectory(mhw);
        File.WriteAllBytes(Path.Combine(mhw,"MonsterHunterWorld.exe"),[0x4d,0x5a]);
        var other=Path.Combine(root,"steam-other");var binaries=Path.Combine(other,"SomeGame","Binaries","Win64");Directory.CreateDirectory(binaries);
        File.WriteAllBytes(Path.Combine(binaries,"SomeGame.exe"),[0x4d,0x5a,0x01]);

        var discoveryCalls=0;
        var registry=new GameProfileRegistry(Path.Combine(root,"discover-state"),()=>
        {
            discoveryCalls++;
            return
            [
                new GameDiscoveryCandidate("Monster Hunter: World",mhw,null,"Steam","582010"),
                new GameDiscoveryCandidate("Some Game",other,null,"Steam","123456")
            ];
        });
        var page=new GamesPageViewModel(registry);

        page.Refresh();
        await page.AutomaticDiscoveryTask;
        page.Refresh();
        await page.AutomaticDiscoveryTask;

        Assert.Equal(1,discoveryCalls);
        Assert.Equal(2,page.Rows.Count);
        Assert.Contains(page.Rows,x=>x.IsMonsterHunterWorld&&x.SteamAppId=="582010");
        var generic=Assert.Single(page.Rows,x=>x.DisplayName=="Some Game");
        Assert.Equal("Steam",generic.Store);
        Assert.Equal("123456",generic.SteamAppId);
        Assert.Equal(Path.GetFullPath(other),generic.GameRoot,StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"SomeGame\Binaries\Win64\SomeGame.exe",generic.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discovery_repairs_stale_same_root_profile_without_changing_active_game()
    {
        var game=Path.Combine(root,"repair-game");Directory.CreateDirectory(game);
        var liveExe=Path.Combine(game,"SomeGame.exe");File.WriteAllBytes(liveExe,[0x4d,0x5a]);
        var activeRoot=Path.Combine(root,"active-game");Directory.CreateDirectory(activeRoot);
        var activeExe=Path.Combine(activeRoot,"Active.exe");File.WriteAllBytes(activeExe,[0x4d,0x5a]);

        var registry=new GameProfileRegistry(Path.Combine(root,"repair-state"),()=>
        [
            new GameDiscoveryCandidate("Some Game",game,null,"Steam","123456")
        ]);
        var stale=GameProfile.Generic("some-game","Some Game",game,"Missing.exe","Mods");
        var active=GameProfile.Generic("active-game","Active Game",activeRoot,"Active.exe","");
        registry.Upsert(stale);
        registry.Upsert(active);
        registry.SetActive(active.Id);

        var changed=registry.DiscoverAndRegisterInstalledGames();

        var repaired=Assert.Single(changed);
        Assert.Equal(stale.Id,repaired.Id);
        Assert.Equal("SomeGame.exe",repaired.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("SomeGame",repaired.ProcessName);
        Assert.Equal("Steam",repaired.Store);
        Assert.Equal("123456",repaired.SteamAppId);
        Assert.Equal(active.Id,registry.GetActive()!.Id);
        var persisted=Assert.Single(registry.Load(),x=>x.Id==stale.Id);
        Assert.Equal("SomeGame.exe",persisted.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discovery_keeps_live_same_root_profile_without_duplicate_or_repair()
    {
        var game=Path.Combine(root,"live-game");Directory.CreateDirectory(game);
        var originalExe=Path.Combine(game,"Original.exe");File.WriteAllBytes(originalExe,[0x4d,0x5a]);
        var replacementExe=Path.Combine(game,"Replacement.exe");File.WriteAllBytes(replacementExe,[0x4d,0x5a,0x01]);

        var registry=new GameProfileRegistry(Path.Combine(root,"live-state"),()=>
        [
            new GameDiscoveryCandidate("Live Game",game,replacementExe,"Steam","222222")
        ]);
        var existing=GameProfile.Generic("live-game","Live Game",game,"Original.exe","");
        registry.Upsert(existing);

        Assert.Empty(registry.DiscoverAndRegisterInstalledGames());

        var persisted=Assert.Single(registry.Load());
        Assert.Equal(existing.Id,persisted.Id);
        Assert.Equal("Original.exe",persisted.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Null(persisted.SteamAppId);
    }

    [Fact]
    public void Discovery_canonicalizes_live_generic_MHW_profile_when_canonical_executable_is_discovered()
    {
        var game=Path.Combine(root,"mhw-live-generic");Directory.CreateDirectory(game);
        var launcher=Path.Combine(game,"Launcher.exe");File.WriteAllBytes(launcher,[0x4d,0x5a]);
        var mhwExe=Path.Combine(game,"MonsterHunterWorld.exe");File.WriteAllBytes(mhwExe,[0x4d,0x5a,0x01]);
        var stateRoot=Path.Combine(root,"mhw-live-generic-state");
        GameDiscoveryCandidate[] Candidates() => [new GameDiscoveryCandidate("Monster Hunter: World",game,mhwExe,"Steam","582010")];
        var registry=new GameProfileRegistry(stateRoot,Candidates);
        var generic=GameProfile.Generic("legacy-live-mhw","My Monster Hunter",game,"Launcher.exe","Mods",store:"Manual") with { SavePath="user-save-location" };
        registry.Upsert(generic);

        var first=registry.DiscoverAndRegisterInstalledGamesDetailed();

        Assert.Empty(first.Added);
        var repaired=Assert.Single(first.Repaired);
        Assert.Equal(generic.Id,repaired.Id);
        Assert.Equal(generic.DisplayName,repaired.DisplayName);
        Assert.Equal(generic.SavePath,repaired.SavePath);
        Assert.Equal("Manual",repaired.Store);
        Assert.True(repaired.IsMonsterHunterWorld);
        Assert.Equal(GameSupportTier.AdapterEnhanced,repaired.SupportTier);
        Assert.Equal("mhw",repaired.AdapterId);
        Assert.Equal("MonsterHunterWorld.exe",repaired.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("MonsterHunterWorld",repaired.ProcessName);
        Assert.Equal("nativePC",repaired.ModRootRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("monsterhunterworld",repaired.NexusGameDomain);
        Assert.Equal("582010",repaired.SteamAppId);
        Assert.Equal(9081,repaired.GameBananaGameId);
        Assert.True(repaired.SupportsSemanticCoverage);
        var restarted=new GameProfileRegistry(stateRoot,Candidates);
        Assert.False(restarted.DiscoverAndRegisterInstalledGamesDetailed().HasChanges);
        Assert.Equal(repaired,Assert.Single(restarted.Load()));
    }

    [Fact]
    public void Discovery_refuses_new_MHW_Steam_candidate_without_canonical_executable()
    {
        var game=Path.Combine(root,"mhw-new-wrong-exe");Directory.CreateDirectory(game);
        var launcher=Path.Combine(game,"Launcher.exe");File.WriteAllBytes(launcher,[0x4d,0x5a]);
        var stateRoot=Path.Combine(root,"mhw-new-wrong-exe-state");
        var registry=new GameProfileRegistry(stateRoot,()=> [new GameDiscoveryCandidate("Monster Hunter: World",game,launcher,"Steam","582010")]);

        var result=registry.DiscoverAndRegisterInstalledGamesDetailed();

        Assert.False(result.HasChanges);
        Assert.Empty(result.Added);
        Assert.Empty(result.Repaired);
        Assert.Empty(registry.Load());
        Assert.False(File.Exists(registry.RegistryPath));
    }

    [Fact]
    public void Discovery_refuses_to_repair_stale_MHW_profile_with_non_MHW_executable()
    {
        var game=Path.Combine(root,"mhw-stale");Directory.CreateDirectory(game);
        var wrongExe=Path.Combine(game,"Launcher.exe");File.WriteAllBytes(wrongExe,[0x4d,0x5a]);

        var registry=new GameProfileRegistry(Path.Combine(root,"mhw-stale-state"),()=>
        [
            new GameDiscoveryCandidate("Monster Hunter: World",game,wrongExe,"Steam","582010")
        ]);
        registry.Upsert(GameProfile.MonsterHunterWorld(game));

        Assert.Empty(registry.DiscoverAndRegisterInstalledGames());

        var persisted=Assert.Single(registry.Load());
        Assert.True(persisted.IsMonsterHunterWorld);
        Assert.Equal("MonsterHunterWorld.exe",persisted.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.False(File.Exists(persisted.ExecutablePath));
    }

    [Fact]
    public void Discovery_refuses_MHW_Steam_candidate_repair_even_when_stale_profile_was_generic()
    {
        var game=Path.Combine(root,"mhw-generic-stale");Directory.CreateDirectory(game);
        var wrongExe=Path.Combine(game,"Launcher.exe");File.WriteAllBytes(wrongExe,[0x4d,0x5a]);

        var registry=new GameProfileRegistry(Path.Combine(root,"mhw-generic-state"),()=>
        [
            new GameDiscoveryCandidate("Monster Hunter: World",game,wrongExe,"Steam","582010")
        ]);
        var staleGeneric=GameProfile.Generic("legacy-mhw","Monster Hunter: World",game,"Missing.exe","nativePC");
        registry.Upsert(staleGeneric);

        Assert.Empty(registry.DiscoverAndRegisterInstalledGames());

        var persisted=Assert.Single(registry.Load());
        Assert.Equal(staleGeneric.Id,persisted.Id);
        Assert.False(persisted.IsMonsterHunterWorld);
        Assert.Equal("Missing.exe",persisted.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Null(persisted.SteamAppId);
    }

    [Fact]
    public void Discovery_rebuilds_stale_generic_MHW_profile_from_canonical_adapter_shape()
    {
        var game=Path.Combine(root,"mhw-canonical-repair");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"MonsterHunterWorld.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);

        var registry=new GameProfileRegistry(Path.Combine(root,"mhw-canonical-repair-state"),()=>
        [
            new GameDiscoveryCandidate("Monster Hunter: World",game,exe,"Steam","582010")
        ]);
        var stale=GameProfile.Generic("legacy-mhw","My Monster Hunter",game,"Missing.exe","Mods",store:"Manual") with
        {
            SavePath="user-save-location"
        };
        registry.Upsert(stale);

        var result=registry.DiscoverAndRegisterInstalledGamesDetailed();

        Assert.Empty(result.Added);
        var repaired=Assert.Single(result.Repaired);
        Assert.Equal(stale.Id,repaired.Id);
        Assert.Equal(stale.DisplayName,repaired.DisplayName);
        Assert.Equal(stale.SavePath,repaired.SavePath);
        Assert.Equal("Manual",repaired.Store);
        Assert.True(repaired.IsMonsterHunterWorld);
        Assert.Equal(GameSupportTier.AdapterEnhanced,repaired.SupportTier);
        Assert.Equal("mhw",repaired.AdapterId);
        Assert.Equal("MonsterHunterWorld.exe",repaired.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("MonsterHunterWorld",repaired.ProcessName);
        Assert.Equal("nativePC",repaired.ModRootRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("monsterhunterworld",repaired.NexusGameDomain);
        Assert.Equal("582010",repaired.SteamAppId);
        Assert.Equal(9081,repaired.GameBananaGameId);
        Assert.True(repaired.SupportsSemanticCoverage);

        var persisted=Assert.Single(registry.Load());
        Assert.Equal(repaired,persisted);
    }

    [Fact]
    public void Discovery_checks_all_same_root_profiles_before_repairing_a_stale_sibling()
    {
        var game=Path.Combine(root,"same-root-set");Directory.CreateDirectory(game);
        var liveExe=Path.Combine(game,"Live.exe");File.WriteAllBytes(liveExe,[0x4d,0x5a]);
        var replacementExe=Path.Combine(game,"Replacement.exe");File.WriteAllBytes(replacementExe,[0x4d,0x5a,0x01]);

        var registry=new GameProfileRegistry(Path.Combine(root,"same-root-set-state"),()=>
        [
            new GameDiscoveryCandidate("Same Root Game",game,replacementExe,"Steam","333333")
        ]);
        var stale=GameProfile.Generic("a-stale","A stale profile",game,"Missing.exe","");
        var live=GameProfile.Generic("z-live","Z live profile",game,"Live.exe","");
        registry.Upsert(stale);
        registry.Upsert(live);
        registry.SetActive(stale.Id);

        var result=registry.DiscoverAndRegisterInstalledGamesDetailed();

        Assert.Empty(result.Added);
        var reconciled=Assert.Single(result.Repaired);
        Assert.Equal(live.Id,reconciled.Id);
        var persisted=Assert.Single(registry.Load());
        Assert.Equal(live.Id,persisted.Id);
        Assert.Equal("Live.exe",persisted.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal(live.Id,registry.GetActive()!.Id);
    }

    [Fact]
    public void Discovery_leaves_registry_unchanged_when_active_duplicate_cannot_be_repointed()
    {
        var game=Path.Combine(root,"same-root-repoint-failure");Directory.CreateDirectory(game);
        var liveExe=Path.Combine(game,"Live.exe");File.WriteAllBytes(liveExe,[0x4d,0x5a]);

        var stateRoot=Path.Combine(root,"same-root-repoint-failure-state");
        var registry=new GameProfileRegistry(stateRoot,()=>
        [
            new GameDiscoveryCandidate("Same Root Game",game,liveExe,"Steam","333333")
        ]);
        var stale=GameProfile.Generic("a-stale-locked","A stale locked",game,"Missing.exe","");
        var live=GameProfile.Generic("z-live-owner","Z live owner",game,"Live.exe","");
        registry.Upsert(stale);
        registry.Upsert(live);
        registry.SetActive(stale.Id);

        var activePath=Path.Combine(stateRoot,"active-game.txt");
        using(var locked=new FileStream(activePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            var result=registry.DiscoverAndRegisterInstalledGamesDetailed();
            Assert.False(result.HasChanges);
            Assert.Empty(result.Added);
            Assert.Empty(result.Repaired);
            var persisted=registry.Load();
            Assert.Equal(2,persisted.Count);
            Assert.Contains(persisted,x=>x.Id==stale.Id);
            Assert.Contains(persisted,x=>x.Id==live.Id);
        }

        var retry=registry.DiscoverAndRegisterInstalledGamesDetailed();
        Assert.Single(retry.Repaired);
        var final=Assert.Single(registry.Load());
        Assert.Equal(live.Id,final.Id);
        Assert.Equal(live.Id,registry.GetActive()!.Id);
    }

    [Fact]
    public void Discovery_prefers_stale_MHW_identity_when_all_same_root_profiles_need_repair()
    {
        var game=Path.Combine(root,"same-root-stale-mhw");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"MonsterHunterWorld.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);

        var registry=new GameProfileRegistry(Path.Combine(root,"same-root-stale-mhw-state"),()=>
        [
            new GameDiscoveryCandidate("Monster Hunter: World",game,exe,"Steam","582010")
        ]);
        var generic=GameProfile.Generic("a-generic","A generic stale",game,"MissingGeneric.exe","");
        var mhw=GameProfile.MonsterHunterWorld(game) with
        {
            Id="z-mhw",
            DisplayName="Z canonical stale",
            ExecutableRelativePath="MissingMhw.exe",
            ProcessName="MissingMhw"
        };
        registry.Upsert(generic);
        registry.Upsert(mhw);

        var result=registry.DiscoverAndRegisterInstalledGamesDetailed();

        var repaired=Assert.Single(result.Repaired);
        Assert.Equal(mhw.Id,repaired.Id);
        Assert.True(repaired.IsMonsterHunterWorld);
        Assert.Equal("MonsterHunterWorld.exe",repaired.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
        var persisted=Assert.Single(registry.Load());
        Assert.Equal(mhw.Id,persisted.Id);
        Assert.True(persisted.IsMonsterHunterWorld);
        Assert.DoesNotContain(registry.Load(),x=>x.Id==generic.Id);
    }

    [Fact]
    public void Manual_game_scan_status_distinguishes_added_repaired_mixed_and_zero_change_results()
    {
        var formatter=typeof(MainWindowViewModel).GetMethod(
            "FormatGameDiscoveryStatus",
            System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Assert.NotNull(formatter);

        string Format(int added,int repaired)=>(string)formatter!.Invoke(null,[added,repaired])!;

        Assert.Equal(
            "No new games were found automatically. You can still choose Add Game and select the game executable yourself.",
            Format(0,0));
        Assert.Equal("Added 2 game(s). Select one and choose Use This Game.",Format(2,0));
        Assert.Equal("Repaired 1 existing game profile(s). Select one and choose Use This Game.",Format(0,1));
        Assert.Equal("Added 2 game(s) and repaired 1 existing game profile(s). Select one and choose Use This Game.",Format(2,1));
    }

    [Fact]
    public void Discovery_repair_is_idempotent_after_persisted_restart()
    {
        var game=Path.Combine(root,"repair-idempotent");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"MonsterHunterWorld.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        var stateRoot=Path.Combine(root,"repair-idempotent-state");
        GameDiscoveryCandidate[] Candidates() =>
        [
            new GameDiscoveryCandidate("Monster Hunter: World",game,exe,"Steam","582010")
        ];

        var firstRegistry=new GameProfileRegistry(stateRoot,Candidates);
        var stale=GameProfile.Generic("legacy-id","Legacy",game,"Missing.exe","Mods");
        firstRegistry.Upsert(stale);

        var first=firstRegistry.DiscoverAndRegisterInstalledGamesDetailed();
        Assert.Empty(first.Added);
        var repaired=Assert.Single(first.Repaired);
        Assert.Equal(stale.Id,repaired.Id);
        Assert.True(repaired.IsMonsterHunterWorld);

        var restartedRegistry=new GameProfileRegistry(stateRoot,Candidates);
        var second=restartedRegistry.DiscoverAndRegisterInstalledGamesDetailed();

        Assert.False(second.HasChanges);
        Assert.Empty(second.Added);
        Assert.Empty(second.Repaired);
        var persisted=Assert.Single(restartedRegistry.Load());
        Assert.Equal(stale.Id,persisted.Id);
        Assert.True(persisted.IsMonsterHunterWorld);
        Assert.Equal("MonsterHunterWorld.exe",persisted.ExecutableRelativePath,StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steam_discovery_reads_every_manifest_across_configured_libraries()
    {
        var steam=Path.Combine(root,"Steam");
        var secondary=Path.Combine(root,"SteamLibrary");
        Directory.CreateDirectory(Path.Combine(steam,"steamapps","common","First Game"));
        Directory.CreateDirectory(Path.Combine(secondary,"steamapps","common","Second Game"));
        var escapedSecondary=secondary.Replace("\\","\\\\",StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),$"\"libraryfolders\"\n{{\n  \"1\"\n  {{\n    \"path\" \"{escapedSecondary}\"\n  }}\n}}");
        File.WriteAllText(Path.Combine(steam,"steamapps","appmanifest_111.acf"),"\"AppState\"\n{\n\"appid\" \"111\"\n\"name\" \"First Game\"\n\"installdir\" \"First Game\"\n}");
        File.WriteAllText(Path.Combine(secondary,"steamapps","appmanifest_222.acf"),"\"AppState\"\n{\n\"appid\" \"222\"\n\"name\" \"Second Game\"\n\"installdir\" \"Second Game\"\n}");

        var found=GameProfileRegistry.DiscoverSteamFromRoots([steam]);

        Assert.Equal(2,found.Count);
        Assert.Contains(found,x=>x.Name=="First Game"&&x.SteamAppId=="111");
        Assert.Contains(found,x=>x.Name=="Second Game"&&x.SteamAppId=="222");
    }

    [Fact]
    public void Xbox_discovery_uses_Content_root_and_ignores_helper_executables()
    {
        var xbox=Path.Combine(root,"XboxGames");
        var content=Path.Combine(xbox,"Crash Bandicoot 4","Content");
        var binaries=Path.Combine(content,"CrashBandicoot4","Binaries","Win64");
        Directory.CreateDirectory(binaries);
        Directory.CreateDirectory(Path.Combine(content,"EasyAntiCheat"));
        File.WriteAllBytes(Path.Combine(content,"EasyAntiCheat","EasyAntiCheat.exe"),[0x4d,0x5a,0x02,0x03]);
        File.WriteAllBytes(Path.Combine(binaries,"CrashReportClient.exe"),[0x4d,0x5a,0x02,0x03,0x04]);
        var gameExe=Path.Combine(binaries,"CrashBandicoot4.exe");
        File.WriteAllBytes(gameExe,[0x4d,0x5a]);

        var found=GameProfileRegistry.DiscoverXboxRoots([xbox]);

        var game=Assert.Single(found);
        Assert.Equal("Crash Bandicoot 4",game.Name);
        Assert.Equal("Xbox",game.Store);
        Assert.Equal(Path.GetFullPath(content),Path.GetFullPath(game.Root),StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath(gameExe),Path.GetFullPath(game.Executable!),StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_can_add_and_activate_arbitrary_windows_game()
    {
        var game=Path.Combine(root,"game");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"Example.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        Directory.CreateDirectory(Path.Combine(game,"Mods"));
        var registry=new GameProfileRegistry(Path.Combine(root,"state"));
        var profile=registry.AddGenericFromExecutable(exe);
        Assert.Equal("Example",profile.DisplayName);
        Assert.Equal("Mods",profile.ModRootRelativePath,StringComparer.OrdinalIgnoreCase);
        Assert.Equal(profile.Id,registry.GetActive()!.Id);
    }

    [Theory]
    [InlineData(@"..\\..\\escaped")]
    [InlineData(@"bad\\child")]
    [InlineData("bad/child")]
    [InlineData("Not Canonical")]
    [InlineData("UPPERCASE")]
    public void Registry_rejects_noncanonical_persisted_ids_without_rewriting_registry(string id)
    {
        var game=Path.Combine(root,"registry-game");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"Game.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        var profile=GameProfile.Generic("safe-game","Safe Game",game,"Game.exe","") with { Id=id };
        var state=Path.Combine(root,"registry-state");Directory.CreateDirectory(state);
        var registry=new GameProfileRegistry(state);
        var json=JsonSerializer.Serialize(new[]{profile},JsonOptions);
        File.WriteAllText(registry.RegistryPath,json);

        Assert.Empty(registry.Load());
        Assert.Equal(json,File.ReadAllText(registry.RegistryPath));
    }

    [Fact]
    public void App_paths_ignore_malicious_active_profile_before_creating_workspace_paths()
    {
        var tool=Path.Combine(root,"tool");var state=Path.Combine(tool,"State");Directory.CreateDirectory(state);
        var game=Path.Combine(root,"app-game");Directory.CreateDirectory(game);
        var exe=Path.Combine(game,"Game.exe");File.WriteAllBytes(exe,[0x4d,0x5a]);
        var safe=GameProfile.Generic("safe-game","Safe Game",game,"Game.exe","");
        var malicious=safe with { Id=@"..\\..\\escaped-workspace", DisplayName="Malicious" };
        var registry=new GameProfileRegistry(state);
        var json=JsonSerializer.Serialize(new[]{malicious,safe},JsonOptions);
        File.WriteAllText(registry.RegistryPath,json);File.WriteAllText(Path.Combine(state,"active-game.txt"),malicious.Id);
        var escapedWorkspace=Path.Combine(root,"escaped-workspace");Directory.CreateDirectory(escapedWorkspace);
        var sentinel=Path.Combine(escapedWorkspace,"sentinel.txt");File.WriteAllText(sentinel,"keep");
        var oldHome=Environment.GetEnvironmentVariable("MOD_MANAGER_HOME");
        var oldLegacyHome=Environment.GetEnvironmentVariable("MHW_MANAGER_HOME");
        try
        {
            Environment.SetEnvironmentVariable("MOD_MANAGER_HOME",tool);Environment.SetEnvironmentVariable("MHW_MANAGER_HOME",null);
            var paths=AppPaths.Discover();
            Assert.Equal(safe.Id,paths.Game.Id);
            Assert.False(Directory.Exists(Path.Combine(escapedWorkspace,"Mods")));
            Assert.False(Directory.Exists(Path.Combine(tool,"escaped-workspace","Next")));
            Assert.Equal("keep",File.ReadAllText(sentinel));
            Assert.Equal(json,File.ReadAllText(registry.RegistryPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOD_MANAGER_HOME",oldHome);Environment.SetEnvironmentVariable("MHW_MANAGER_HOME",oldLegacyHome);
        }
    }

    [Fact]
    public async Task Generic_scanner_maps_bare_package_into_configured_mod_root()
    {
        var game=Path.Combine(root,"game2");Directory.CreateDirectory(Path.Combine(game,"Mods"));
        var exe=Path.Combine(game,"Game.exe");await File.WriteAllBytesAsync(exe,[0x4d,0x5a],TestToken);
        var profile=GameProfile.Generic("g","G",game,"Game.exe","Mods");
        var state=Path.Combine(root,"state2");var db=new ManagerDatabase(Path.Combine(state,"manager.db"));await db.InitializeAsync(TestToken);
        var hashing=new HashingService();var blobs=new BlobStore(Path.Combine(state,"blobs"),db);var scanner=new ModScanner(db,blobs,hashing,profile);
        var source=Path.Combine(root,"package");Directory.CreateDirectory(source);await File.WriteAllTextAsync(Path.Combine(source,"hello.pak"),"x",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        var files=await scanner.CaptureAsync(mod,TestToken);
        Assert.Equal(@"root\Mods\hello.pak",Assert.Single(files).Path);
    }

    [Fact]
    public async Task Generic_scanner_preserves_archive_that_already_contains_target_folder()
    {
        var game=Path.Combine(root,"game3");Directory.CreateDirectory(Path.Combine(game,"Mods"));
        var exe=Path.Combine(game,"Game.exe");await File.WriteAllBytesAsync(exe,[0x4d,0x5a],TestToken);
        var profile=GameProfile.Generic("g","G",game,"Game.exe","Mods");
        var state=Path.Combine(root,"state3");var db=new ManagerDatabase(Path.Combine(state,"manager.db"));await db.InitializeAsync(TestToken);
        var scanner=new ModScanner(db,new BlobStore(Path.Combine(state,"blobs"),db),new HashingService(),profile);
        var source=Path.Combine(root,"package3");Directory.CreateDirectory(Path.Combine(source,"Mods","Foo"));await File.WriteAllTextAsync(Path.Combine(source,"Mods","Foo","x.dll"),"x",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        var files=await scanner.CaptureAsync(mod,TestToken);
        Assert.Equal(@"root\Mods\Foo\x.dll",Assert.Single(files).Path);
    }

    [Theory]
    [InlineData("", @"root\nativePC\asset.bin")]
    [InlineData("Mods", @"root\Mods\nativePC\asset.bin")]
    public async Task Generic_scanner_does_not_drop_MHW_named_folders(string target, string expected)
    {
        var profile=GameProfile.Generic("g","G",Path.Combine(root,"game"),"Game.exe",target);
        var db=new ManagerDatabase(Path.Combine(root,"state","manager.db"));await db.InitializeAsync(TestToken);
        var scanner=new ModScanner(db,new BlobStore(Path.Combine(root,"blobs"),db),new HashingService(),profile);
        var source=Path.Combine(root,"package");Directory.CreateDirectory(Path.Combine(source,"nativePC"));
        await File.WriteAllTextAsync(Path.Combine(source,"nativePC","asset.bin"),"asset",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        Assert.Equal(expected,Assert.Single(await scanner.CaptureAsync(mod,TestToken)).Path);
    }

    [Fact]
    public async Task Rescan_recaptures_a_missing_cached_blob()
    {
        var profile=GameProfile.Generic("g","G",Path.Combine(root,"game"),"Game.exe","");
        var db=new ManagerDatabase(Path.Combine(root,"state","manager.db"));await db.InitializeAsync(TestToken);
        var blobs=new BlobStore(Path.Combine(root,"blobs"),db);
        var scanner=new ModScanner(db,blobs,new HashingService(),profile);
        var source=Path.Combine(root,"package");Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source,"asset.bin"),"unchanged bytes",TestToken);
        var mod=new ModDescriptor("m","M","M",source,false,0);await db.UpsertModAsync(mod,TestToken);
        var first=Assert.Single(await scanner.CaptureAsync(mod,TestToken));
        File.Delete(blobs.PathFor(first.BlobSha256));
        var second=Assert.Single(await scanner.CaptureAsync(mod,TestToken));
        Assert.Equal(first.BlobSha256,second.BlobSha256);
        Assert.Equal("unchanged bytes",await File.ReadAllTextAsync(blobs.PathFor(second.BlobSha256),TestToken));
    }
}
