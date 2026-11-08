using System.IO;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Regresses the v8.8.102 incident: a handoff-manifest-only repair on protected
/// main must trigger the canonical release gate. An earlier source-only candidate
/// gate can pass while canonical VERSION.txt / handoff parity is still red.
/// </summary>
public sealed class WindowsReleaseTriggerCoverageTests
{
    [Fact]
    public void Canonical_release_gate_rechecks_handoff_version_repairs_without_evidence_loop()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MhwModManager.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var workflow = File.ReadAllText(Path.Combine(root!.FullName, ".github", "workflows",
            "windows-release-gate.yml"));
        Assert.Contains("      - 'VERSION.txt'", workflow, StringComparison.Ordinal);
        Assert.Contains("      - '_AGENT_CONTEXT/handoff-manifest.json'", workflow,
            StringComparison.Ordinal);
        Assert.Contains("      - '.github/workflows/windows-release-gate.yml'", workflow,
            StringComparison.Ordinal);

        // The release gate itself stages exact-source verification evidence as
        // an ordinary PR that changes these two files. Matching them in the
        // release push paths would create a recursive release-evidence loop.
        Assert.DoesNotContain("      - '_AGENT_CONTEXT/CURRENT_REVISION.json'", workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("      - '_AGENT_CONTEXT/CURRENT_STATE.md'", workflow,
            StringComparison.Ordinal);
    }
}
