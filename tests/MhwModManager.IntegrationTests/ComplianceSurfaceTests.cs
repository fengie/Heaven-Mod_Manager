using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ComplianceSurfaceTests
{
    private static readonly string[] BundledPolicies =
    [
        "PRIVACY.md",
        "TERMS.md",
        "REFUND_POLICY.md",
        "COOKIE_POLICY.md",
        "DATA-DELETION.md",
        "THIRD_PARTY_NOTICES.md",
        "SUPPORT.md"
    ];

    [Fact]
    public void RequiredComplianceDocumentsExistAndAreBundled()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MhwModManager.App.csproj"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));

        foreach (var policy in BundledPolicies)
        {
            Assert.True(File.Exists(Path.Combine(root, policy)), $"Missing compliance document: {policy}");
            Assert.Contains($"<Link>{policy}</Link>", project);
        }

        Assert.Contains("OpenPrivacyPolicyCommand", xaml);
        Assert.Contains("OpenTermsOfUseCommand", xaml);
        Assert.Contains("OpenRefundPolicyCommand", xaml);
        Assert.Contains("OpenCookiePolicyCommand", xaml);
        Assert.Contains("OpenThirdPartyNoticesCommand", xaml);
        Assert.Contains("OpenDataDeletionGuideCommand", xaml);
        Assert.Contains("OpenSupportDetailsCommand", xaml);
        Assert.Contains("OpenAppDataFolderCommand", xaml);
        Assert.Contains("Text=\"{Binding LegalStatusText}\"", xaml);
    }

    [Fact]
    public void ThirdPartyNoticeTracksEveryCentralPackageVersion()
    {
        var root = FindRepositoryRoot();
        var packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        var notices = File.ReadAllText(Path.Combine(root, "THIRD_PARTY_NOTICES.md"));

        var matches = Regex.Matches(
            packages,
            "<PackageVersion Include=\"(?<name>[^\"]+)\" Version=\"(?<version>[^\"]+)\"\\s*/>",
            RegexOptions.CultureInvariant);

        Assert.NotEmpty(matches.Cast<Match>());
        foreach (Match match in matches)
        {
            var name = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;
            Assert.Contains($"| {name} | {version} |", notices);
        }
    }

    [Fact]
    public void NativeAppDocumentsWebOnlyConsentTriggersInsteadOfShowingFakeConsent()
    {
        var root = FindRepositoryRoot();
        var privacy = File.ReadAllText(Path.Combine(root, "PRIVACY.md"));
        var cookies = File.ReadAllText(Path.Combine(root, "COOKIE_POLICY.md"));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "COMPLIANCE-BASELINE.md"));

        Assert.Contains("does not set browser cookies", cookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("marketing email", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Adding cookie-setting embedded web content requires consent/policy work before release.", matrix);
        Assert.Contains("A future marketing-email feature requires unsubscribe handling before release.", matrix);
        Assert.Contains("If that changes, child-data/age-consent review becomes a release gate.", matrix);
    }

    [Fact]
    public void PreviewAndArtworkImagesExposeAutomationNames()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var images = Regex.Matches(xaml, "<Image\\b[^>]*?/>", RegexOptions.CultureInvariant | RegexOptions.Singleline)
            .Cast<Match>()
            .Select(match => match.Value)
            .ToArray();

        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.Contains("AutomationProperties.Name=", image));
    }

    [Fact]
    public void KeyboardNavigationAndVisibleFocusRemainExplicit()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "MainWindow.xaml"));
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "App.xaml"));

        Assert.Contains("KeyboardNavigation.TabNavigation=\"Continue\"", mainXaml);
        Assert.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", appXaml);
        Assert.Contains("Property=\"BorderBrush\" Value=\"{StaticResource FocusRing}\"", appXaml);
        Assert.Contains("<KeyBinding Key=\"Enter\" Modifiers=\"Control\"", mainXaml);
        Assert.Contains("<KeyBinding Key=\"F5\"", mainXaml);
    }

    [Fact]
    public void CoreTextPaletteMeetsWcagAaContrast()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.App", "App.xaml"));

        var text = ReadColor(appXaml, "TextColor");
        var muted = ReadColor(appXaml, "MutedColor");
        var backgrounds = new[]
        {
            ReadColor(appXaml, "BgColor"),
            ReadColor(appXaml, "SidebarColor"),
            ReadColor(appXaml, "SurfaceColor"),
            ReadColor(appXaml, "Surface2Color"),
            ReadColor(appXaml, "Surface3Color")
        };

        foreach (var background in backgrounds)
        {
            Assert.True(Contrast(text, background) >= 4.5, $"TextColor contrast failed against {background}.");
            Assert.True(Contrast(muted, background) >= 4.5, $"MutedColor contrast failed against {background}.");
        }

        Assert.True(Contrast("#07110F", ReadColor(appXaml, "AccentColor")) >= 4.5);
        Assert.True(Contrast("#07110F", ReadColor(appXaml, "AccentBrightColor")) >= 4.5);
        Assert.True(Contrast("#24180A", ReadColor(appXaml, "WarnColor")) >= 4.5);
    }

    private static string ReadColor(string xaml, string key)
    {
        var match = Regex.Match(
            xaml,
            $"<Color x:Key=\"{Regex.Escape(key)}\">(?<value>#[0-9A-Fa-f]{{6}})</Color>",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing color resource {key}.");
        return match.Groups["value"].Value;
    }

    private static double Contrast(string foreground, string background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = hex.TrimStart('#');
        var r = int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var g = int.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        var b = int.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;

        static double Linearize(double channel) =>
            channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);

        return (0.2126 * Linearize(r)) + (0.7152 * Linearize(g)) + (0.0722 * Linearize(b));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MhwModManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
