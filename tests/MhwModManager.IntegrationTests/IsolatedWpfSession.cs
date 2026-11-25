using System.Reflection;
using System.Windows.Threading;
using MhwModManager.App;
using MhwModManager.Core;
using MhwModManager.Diagnostics;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

internal static class IsolatedWpfSession
{
    internal static async Task RunAsync(Func<AppServices,CancellationToken,Task> run, GameProfile? profile = null)
    {
        var token = TestContext.Current.CancellationToken;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () => {
                var root = Directory.CreateTempSubdirectory("manager-wpf-session-");
                AppServices? services = null;
                Exception? failure = null;
                try
                {
                    var game = profile is null ? GameProfile.Generic("catalog-fixture", "Catalog fixture", Path.Combine(root.FullName, "game"), "Fixture.exe", "Mods") : DiscoveredGameProfiles.Isolate(profile, root.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(game.ExecutablePath)!);
                    File.WriteAllBytes(game.ExecutablePath, [1, 2, 3]);
                    string Folder(string name) => Directory.CreateDirectory(Path.Combine(root.FullName,name)).FullName;
                    var paths = new AppPaths(root.FullName,Folder("workspace"),Folder("mods"),Folder("inbox"),Folder("archives"),
                        Folder("state"),Folder("next"),Path.Combine(root.FullName,"session.db"),Folder("blobs"),game.GameRoot,game,DownloadsRoot:Folder("downloads"));
                    var bootstrap = typeof(App.App).GetMethod("LoadGameSessionAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                    // Startup diagnostics configure the process-wide writer. Keep its existing
                    // destination independent of the disposable per-game data and lifetime.
                    var diagnosticRoot = MasterDebugLog.RootDirectory;
                    var startup = StartupDiagnosticSession.Start(diagnosticRoot);
                    Assert.Equal(diagnosticRoot, MasterDebugLog.RootDirectory);
                    services = await (Task<AppServices>)bootstrap.Invoke(null,
                        [paths, startup, Array.Empty<string>(), null, null, null, null])!;
                    // These dispatcher fixtures drive commands directly. A live file watcher
                    // also schedules independent telemetry writes to this session database.
                    services.ChangeHints.Dispose();
                    // Restrict inventory discovery to disposable game roots. Production sessions
                    // retain full host discovery; fixture timing must not depend on installed games.
                    services = services with { GameRegistry = new GameProfileRegistry(paths.StateRoot, () => [], registeredInventoryOnly: true) };
                    await run(services, token);
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    try
                    {
                        if (services is not null)
                        {

                            (services.Log as IDisposable)?.Dispose();
                            await using var pool = await services.Database.OpenAsync(CancellationToken.None);
                            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pool);
                        }
                        root.Delete(recursive: true);
                    }
                    catch (Exception ex) { failure = failure is null ? ex : new AggregateException(failure, ex); }
                    if (failure is null) finished.SetResult();
                    else finished.SetException(failure);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromMinutes(3), token);
    }
}
