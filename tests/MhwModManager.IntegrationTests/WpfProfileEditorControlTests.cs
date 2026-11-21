using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using MhwModManager.App;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class WpfProfileEditorControlTests
{
    [Fact]
    public async Task FomodInstallWithMissingChoiceStaysOpenAndAcceptsACorrectedSelection()
    {
        var root = Directory.CreateTempSubdirectory("manager-fomod-control-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "fomod"));
            File.WriteAllText(Path.Combine(root.FullName, "texture.tex"), "fixture");
            File.WriteAllText(Path.Combine(root.FullName, "fomod", "ModuleConfig.xml"), """
                <config><moduleName>Fixture</moduleName><installSteps><installStep name="Texture">
                <optionalFileGroups><group name="Required choice" type="SelectExactlyOne"><plugins>
                <plugin name="Texture"><files><file source="texture.tex" destination="body.tex"/></files>
                <typeDescriptor><type name="Optional"/></typeDescriptor></plugin>
                </plugins></group></optionalFileGroups></installStep></installSteps></config>
                """);
            var service = new FomodInstallerService(root.FullName);
            var profile = GameProfile.MonsterHunterWorld(Path.Combine(root.FullName, "game"));
            await OnUiThread(() => {
                var window = new FomodInstallerWindow(service, profile);
                ExerciseModal(window, () => {
                    // Deliver the actual routed Click synchronously so an escaped validation
                    // exception fails the test instead of reaching the process dispatcher.
                    var install = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "Install Mod"));
                    install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, install));
                    Assert.True(window.IsVisible);
                    Assert.Contains(Descendants(window).OfType<TextBlock>(),
                        label => label.Text.StartsWith("The mod cannot be installed", StringComparison.Ordinal));
                    var radio = Descendants(window).OfType<RadioButton>().Single();
                    var peer = new RadioButtonAutomationPeer(radio);
                    ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                    Assert.Equal("0/0/0", Assert.Single(window.SelectedOptions));
                    InvokeButton(window, "Install Mod");
                });
                Assert.True(window.DialogResult);
                Assert.Single(service.Plan(window.SelectedOptions, profile));
                Assert.False(Directory.Exists(profile.GameRoot));
            });
        }
        finally { root.Delete(recursive: true); }
    }

    public static IEnumerable<object[]> RegisteredIds() =>
        DiscoveredGameProfiles.Load().Select(profile => new object[] { profile.Id });

    [Fact]
    public async Task FomodCheckboxAutomationChangesTheActualSelectionAndHonorsUnavailableOptions()
    {
        var root = Directory.CreateTempSubdirectory("manager-fomod-options-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "fomod"));
            File.WriteAllText(Path.Combine(root.FullName, "fomod", "ModuleConfig.xml"), """
                <config><moduleName>Fixture</moduleName><installSteps><installStep name="Extras">
                <optionalFileGroups><group name="Extras" type="SelectAny"><plugins>
                <plugin name="Unlocked"><typeDescriptor><type name="Optional"/></typeDescriptor></plugin>
                <plugin name="Locked"><typeDescriptor><type name="NotUsable"/></typeDescriptor></plugin>
                </plugins></group></optionalFileGroups></installStep></installSteps></config>
                """);
            var service = new FomodInstallerService(root.FullName);
            var profile = GameProfile.MonsterHunterWorld(Path.Combine(root.FullName, "game"));
            await OnUiThread(() => {
                var window = new FomodInstallerWindow(service, profile);
                ExerciseModal(window, () => {
                    IToggleProvider ToggleFor(string label)
                    {
                        var option = Descendants(window).OfType<CheckBox>().Single(b => b.Content.ToString()!.StartsWith(label, StringComparison.Ordinal));
                        return (IToggleProvider)new CheckBoxAutomationPeer(option).GetPattern(PatternInterface.Toggle);
                    }
                    ToggleFor("Unlocked").Toggle();
                    Assert.Equal("0/0/0", Assert.Single(window.SelectedOptions));
                    Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => ToggleFor("Locked").Toggle());
                    ToggleFor("Unlocked").Toggle();
                    Assert.Empty(window.SelectedOptions);
                    InvokeButton(window, "Cancel");
                });
                Assert.False(window.DialogResult);
                Assert.False(Directory.Exists(profile.GameRoot));
            });
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [MemberData(nameof(RegisteredIds))]
    public async Task ProfileEditorHonorsFieldOwnershipAndReturnsOnlyTheIntendedEdits(string id)
    {
        var original = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load().Single(game => game.Id == id),
            Path.Combine(Path.GetTempPath(), "manager-editor-" + Guid.NewGuid().ToString("N")));
        await OnUiThread(() =>
        {
            var window = new GameProfileEditorWindow(original);
            ExerciseModal(window, () =>
            {
                var name = Field(window, "Game name");
                var folder = Field(window, "Game folder");
                var executable = Field(window, "Game executable (detected)");
                var modRoot = Field(window, "Mod folder (inside the game folder)");
                var nexus = Field(window, "Nexus game name (optional)");
                var save = Field(window, "Save file for backups (optional)");
                Assert.True(folder.IsReadOnly && executable.IsReadOnly);
                Assert.Equal(original.GameRoot, folder.Text);
                Assert.Equal(original.ExecutableRelativePath, executable.Text);
                Assert.Equal(original.IsMonsterHunterWorld, modRoot.IsReadOnly);
                Assert.Equal(original.ModRootRelativePath, modRoot.Text);
                Assert.False(name.IsReadOnly || nexus.IsReadOnly || save.IsReadOnly);
                name.Text = "  Fixture display name  ";
                nexus.Text = "  /fixture-domain/  ";
                save.Text = Path.Combine(original.GameRoot, "fixture.sav");
                if (!modRoot.IsReadOnly) modRoot.Text = "FixtureMods";
                InvokeButton(window, "Save settings");
            });
            Assert.True(window.DialogResult);
            Assert.Equal(original with {
                DisplayName = "Fixture display name", NexusGameDomain = "fixture-domain",
                SavePath = Path.Combine(original.GameRoot, "fixture.sav"),
                ModRootRelativePath = original.IsMonsterHunterWorld ? original.ModRootRelativePath : "FixtureMods"
            }, window.Result);
            Assert.False(Directory.Exists(original.GameRoot));
        });
    }

    [Fact]
    public async Task CancelDiscardsProfileEdits()
    {
        var profile = DiscoveredGameProfiles.Isolate(DiscoveredGameProfiles.Load()[0], Path.GetTempPath());
        await OnUiThread(() =>
        {
            var window = new GameProfileEditorWindow(profile);
            ExerciseModal(window, () => {
                Field(window, "Game name").Text = "Uncommitted";
                InvokeButton(window, "Cancel");
            });
            Assert.False(window.DialogResult);
            Assert.Null(window.Result);
        });
    }

    private static TextBox Field(Window window, string label)
    {
        var caption = Descendants(window).OfType<TextBlock>().Single(block => block.Text == label);
        return Descendants((DependencyObject)caption.Parent).OfType<TextBox>().Single();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }

    private static void InvokeButton(Window window, string caption)
    {
        var button = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, caption));
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static void ExerciseModal(Window window, Action action)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        Exception? failure = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { failure = new TimeoutException("Editor did not close after its action"); window.Close(); };
        window.Loaded += (_, _) => {
            try { action(); }
            catch (Exception ex) { failure = ex; window.Close(); }
        };
        timeout.Start();
        try { window.ShowDialog(); }
        finally { timeout.Stop(); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task OnUiThread(Action action)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try { action(); result.SetResult(); }
            catch (Exception ex) { result.SetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await result.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    }
}
