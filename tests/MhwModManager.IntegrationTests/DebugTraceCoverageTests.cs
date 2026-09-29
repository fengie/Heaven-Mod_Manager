using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class DebugTraceCoverageTests
{
    [Fact]
    public void ExternalProcessesCannotBypassMasterProcessTrace()
    {
        var root = FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(Path.Combine("MhwModManager.Core", "MasterDebugLog.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("Process.Start(", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void CriticalLayersContainMasterTraceMarkers()
    {
        var root = FindRepositoryRoot();
        var required = new[]
        {
            Path.Combine("src", "MhwModManager.Storage", "ManagerDatabase.cs"),
            Path.Combine("src", "MhwModManager.Filesystem", "DeploymentExecutor.cs"),
            Path.Combine("src", "MhwModManager.Filesystem", "AtomicFileOps.cs"),
            Path.Combine("src", "MhwModManager.Automation", "AutomationCoordinator.cs"),
            Path.Combine("src", "MhwModManager.App", "ViewModels", "MainWindowViewModel.cs")
        };
        foreach (var relative in required)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("MasterDebugLog", text, StringComparison.Ordinal);
        }
    }


    [Fact]
    public void MethodTraceScopesCannotBePlacedInsideObjectOrCollectionInitializers()
    {
        var root = FindRepositoryRoot();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("using var __mhwTrace = MasterDebugLog.BeginMethod();", StringComparison.Ordinal)) continue;
                var open = i - 1;
                while (open >= 0 && string.IsNullOrWhiteSpace(lines[open])) open--;
                if (open < 0 || !StringComparer.Ordinal.Equals(lines[open].Trim(), "{")) continue;
                var header = open - 1;
                while (header >= 0 && string.IsNullOrWhiteSpace(lines[header])) header--;
                if (header < 0) continue;
                var declaration = lines[header].Trim();
                Assert.False(
                    declaration.Contains("= new", StringComparison.Ordinal) || declaration.Contains("=new", StringComparison.Ordinal),
                    $"Method trace was inserted inside an initializer at {Path.GetRelativePath(root, path)}:{i + 1}. Header: {declaration}");
            }
        }
    }


    [Fact]
    public void FunctionVerificationPipelineIsFailClosed()
    {
        var root = FindRepositoryRoot();
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "MhwModManager.FunctionVerifier", "Program.cs"));
        var verifyScript = File.ReadAllText(Path.Combine(root, "scripts", "Verify-Release.ps1"));
        var buildScript = File.ReadAllText(Path.Combine(root, "scripts", "Build-Release.ps1"));

        Assert.Contains("CSharpSyntaxTree.ParseText", verifier, StringComparison.Ordinal);
        Assert.Contains("exact-function-cache", verifier, StringComparison.Ordinal);
        Assert.Contains("changed-or-new", verifier, StringComparison.Ordinal);
        Assert.Contains("TraceRequired", verifier, StringComparison.Ordinal);
        Assert.Contains("Promote verified function fingerprints", verifyScript, StringComparison.Ordinal);
        Assert.Contains("Function cache promotion safely skipped", verifyScript, StringComparison.Ordinal);
        Assert.Contains("Promote verified function fingerprints", buildScript, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, ".verification", "function-status.json")));
        Assert.True(File.Exists(Path.Combine(root, ".verification", "trusted-v8.7.0-files.json")));
        Assert.True(File.Exists(Path.Combine(root, ".verification", "trusted-v8.7.0-src.zip")));
    }

    [Fact]
    public void MasterTracePropagatesObservedCallErrorsWithoutSwallowingThem()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.Core", "MasterDebugLog.cs"));
        Assert.Contains("AsyncLocal<ScopeFrame?>", text, StringComparison.Ordinal);
        Assert.Contains("MarkExceptionObserved(ex)", text, StringComparison.Ordinal);
        Assert.Contains("frame.Scope.ObserveException(exception)", text, StringComparison.Ordinal);
        Assert.Contains("PASS-CHECK", text, StringComparison.Ordinal);
        Assert.Contains("PASS-WITH-ERROR-CHECK", text, StringComparison.Ordinal);
        Assert.Contains("ERROR-CHECK", text, StringComparison.Ordinal);
        Assert.Contains("exception-may-have-been-handled", text, StringComparison.Ordinal);
    }


    [Fact]
    public void FunctionVerifierRejectsGeneratedSourceAndValidatesTrustedSnapshot()
    {
        var root = FindRepositoryRoot();
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "MhwModManager.FunctionVerifier", "Program.cs"));
        Assert.Contains("IsGeneratedBuildPath", verifier, StringComparison.Ordinal);
        Assert.Contains("part.Equals(\"obj\"", verifier, StringComparison.Ordinal);
        Assert.Contains("part.Equals(\"bin\"", verifier, StringComparison.Ordinal);
        Assert.Contains("Trusted source snapshot hash mismatch", verifier, StringComparison.Ordinal);
        Assert.Contains("Trusted source snapshot is missing", verifier, StringComparison.Ordinal);
        Assert.Contains("UncoveredExplicitCallSiteCount", verifier, StringComparison.Ordinal);
        Assert.Contains("ExplicitInterface", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentHandoffContinuityIsFailClosedAndPropagating()
    {
        var root = FindRepositoryRoot();
        var start = File.ReadAllText(Path.Combine(root, "NEXT-AGENT-START-HERE.md"));
        var protocol = File.ReadAllText(Path.Combine(root, "_AGENT_CONTEXT", "CONTINUITY_PROTOCOL.md"));
        var preflight = File.ReadAllText(Path.Combine(root, "scripts", "Test-AgentHandoff.ps1"));
        var packager = File.ReadAllText(Path.Combine(root, "scripts", "Build-Source-Handoff.ps1"));
        Assert.Contains("Do not break the chain", start, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not break the chain", protocol, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("propagateToNextAgent", preflight, StringComparison.Ordinal);
        Assert.Contains("SOURCE_HANDOFF_MANIFEST.json", packager, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "_AGENT_CONTEXT", "handoff-manifest.json")));
    }

    [Fact]
    public void FirstChanceDetailLoggingIsOptInButScopeObservationRemainsActive()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.Core", "MasterDebugLog.cs"));
        Assert.Contains("MHW_FIRST_CHANCE_DETAIL", text, StringComparison.Ordinal);
        Assert.Contains("MarkExceptionObserved(ex)", text, StringComparison.Ordinal);
        Assert.Contains("FirstChanceDetailEnabled", text, StringComparison.Ordinal);
        Assert.Contains("handlingFirstChance", text, StringComparison.Ordinal);
        Assert.Contains("firstChanceExceptions=", text, StringComparison.Ordinal);
    }


    [Fact]
    public void RoutineMethodTraceDetailIsOptInWhileErrorObservationRemainsActive()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.Core", "MasterDebugLog.cs"));
        Assert.Contains("MHW_METHOD_TRACE_DETAIL", text, StringComparison.Ordinal);
        Assert.Contains("MethodTraceDetailEnabled", text, StringComparison.Ordinal);
        Assert.Contains("if (verbose || errors != 0)", text, StringComparison.Ordinal);
        Assert.Contains("if (verbose)", text, StringComparison.Ordinal);
        Assert.Contains("ERROR-CHECK", text, StringComparison.Ordinal);
        Assert.Contains("MarkExceptionObserved(ex)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupDiagnosticsBatchJsonWritesButForceFailuresAndCompletion()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "MhwModManager.Diagnostics", "StartupDiagnosticSession.cs"));
        Assert.Contains("RunningReportIntervalTicks", text, StringComparison.Ordinal);
        Assert.Contains("force:completed||entry.Status==StartupDiagnosticStatus.Failed", text, StringComparison.Ordinal);
        Assert.Contains("PersistReport(success ? \"PASS\" : \"FAIL\", summary, force:true);", text, StringComparison.Ordinal);

        var writeStart = text[text.IndexOf("private void WriteStart", StringComparison.Ordinal)..text.IndexOf("private void AddFailure", StringComparison.Ordinal)];
        Assert.DoesNotContain("PersistReport(", writeStart, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
