using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Verifier = MhwModManager.FunctionVerifier.Program;

namespace MhwModManager.IntegrationTests;

public sealed class FunctionVerifierBehaviorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhw-verifier-" + Guid.NewGuid().ToString("N"));
    private string Baseline => Path.Combine(root, ".verification", "function-status.json");

    public FunctionVerifierBehaviorTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, ".verification"));
    }

    [Theory]
    [InlineData("using var trace = MasterDebugLog.BeginMethod(); Work();", 0)]
    [InlineData("using var trace = global::MhwModManager.Core.MasterDebugLog.BeginMethod(); Work();", 0)]
    [InlineData("using (MasterDebugLog.BeginMethod()) { Work(); }", 0)]
    [InlineData("if (false) { using var trace = MasterDebugLog.BeginMethod(); } Work();", 4)]
    [InlineData("System.Action unused = () => { using var trace = MasterDebugLog.BeginMethod(); }; Work();", 4)]
    [InlineData("MasterDebugLog.BeginMethod(); Work();", 4)]
    [InlineData("var trace = MasterDebugLog.BeginMethod(); Work();", 4)]
    [InlineData("Work(); using var trace = MasterDebugLog.BeginMethod();", 4)]
    [InlineData("using (MasterDebugLog.BeginMethod()) { } Work();", 4)]
    [InlineData("using var trace = FakeMasterDebugLog.BeginMethod(); Work();", 4)]
    public void Entry_trace_must_cover_the_executed_body(string body, int expectedExit)
    {
        WriteSource("class C { void Run() { " + body + " } }");
        Assert.Equal(expectedExit, Scan());
    }

    [Theory]
    [InlineData("class C { void Run( {")]
    [InlineData("class C { void Run() { } void Run() { } }")]
    public void Invalid_inventory_preserves_previous_checklist(string invalidSource)
    {
        WriteSource("class C { void Run() { using var trace = MasterDebugLog.BeginMethod(); } }");
        Assert.Equal(0, Scan());
        var previous = File.ReadAllBytes(Baseline);
        WriteSource(invalidSource);
        Assert.Equal(4, Scan());
        Assert.Equal(previous, File.ReadAllBytes(Baseline));
        WriteSource("class C { void Run() { using var trace = MasterDebugLog.BeginMethod(); } }");
        Assert.Equal(0, Scan());
    }

    [Fact]
    public void Missing_trusted_snapshot_cannot_grant_whole_file_trust()
    {
        WriteTrustedFixture(duplicateEntry: false);
        File.Delete(Path.Combine(root, ".verification", "trusted-v8.7.0-src.zip"));
        Assert.Equal(5, Scan());
        Assert.False(File.Exists(Baseline));
    }

    [Fact]
    public void Duplicate_trusted_archive_entries_are_rejected()
    {
        WriteTrustedFixture(duplicateEntry: true);
        Assert.Equal(5, Scan());
        Assert.False(File.Exists(Baseline));
    }

    [Fact]
    public void Scan_keeps_exact_trusted_functions_checked_and_edits_unchecked()
    {
        WriteTrustedFixture(duplicateEntry: false);
        Assert.Equal(0, Scan());
        Assert.True(ReadVerified());
        // Comments do not reopen an exact trusted function.
        WriteSource("// comment\nclass C { void Run() { Work(); } }");
        Assert.Equal(0, Scan());
        Assert.True(ReadVerified());
        WriteSource("class C { void Run() { using var trace = MasterDebugLog.BeginMethod(); Changed(); } }");
        Assert.Equal(0, Scan());
        Assert.False(ReadVerified());
    }

    [Fact]
    public void Repeated_confirm_is_byte_stable_for_unchanged_source()
    {
        File.WriteAllText(Path.Combine(root, "VERSION.txt"), "1.0.0");
        WriteSource("class C { void Run() { using var trace = MasterDebugLog.BeginMethod(); Work(); } }");

        Assert.Equal(0, Confirm());
        var first = File.ReadAllBytes(Baseline);

        Assert.Equal(0, Confirm());
        Assert.Equal(first, File.ReadAllBytes(Baseline));
    }

    [Fact]
    public void Confirm_refreshes_only_changed_function_state_within_the_same_version()
    {
        File.WriteAllText(Path.Combine(root, "VERSION.txt"), "1.0.0");
        WriteSource("class C { void A() { using var trace = MasterDebugLog.BeginMethod(); Work(); } void B() { using var trace = MasterDebugLog.BeginMethod(); Work(); } }");
        Assert.Equal(0, Confirm());
        var aBefore = ReadFunctionState("method:A()");
        var bBefore = ReadFunctionState("method:B()");

        Thread.Sleep(20);
        WriteSource("class C { void A() { using var trace = MasterDebugLog.BeginMethod(); Work(); } void B() { using var trace = MasterDebugLog.BeginMethod(); Changed(); } }");
        Assert.Equal(0, Confirm());
        var aAfter = ReadFunctionState("method:A()");
        var bAfter = ReadFunctionState("method:B()");

        Assert.Equal(aBefore.Fingerprint, aAfter.Fingerprint);
        Assert.Equal(aBefore.VerifiedAtUtc, aAfter.VerifiedAtUtc);
        Assert.NotEqual(bBefore.Fingerprint, bAfter.Fingerprint);
        Assert.NotEqual(bBefore.VerifiedAtUtc, bAfter.VerifiedAtUtc);
    }

    [Fact]
    public void Confirm_refreshes_baseline_when_source_version_changes()
    {
        File.WriteAllText(Path.Combine(root, "VERSION.txt"), "1.0.0");
        WriteSource("class C { void Run() { using var trace = MasterDebugLog.BeginMethod(); Work(); } }");
        Assert.Equal(0, Confirm());
        var before = File.ReadAllBytes(Baseline);

        File.WriteAllText(Path.Combine(root, "VERSION.txt"), "1.0.1");
        Assert.Equal(0, Confirm());
        var after = File.ReadAllBytes(Baseline);

        Assert.NotEqual(before, after);
        using var json = JsonDocument.Parse(after);
        Assert.Equal("1.0.1", json.RootElement.GetProperty("sourceVersion").GetString());
    }

    private void WriteTrustedFixture(bool duplicateEntry)
    {
        const string source = "class C { void Run() { Work(); } }";
        WriteSource(source);
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(root, ".verification", "trusted-v8.7.0-files.json"),
            JsonSerializer.Serialize(new { formatVersion = 1, files = new Dictionary<string, string> { ["src/Fixture.cs"] = hash } }));
        using var archive = ZipFile.Open(Path.Combine(root, ".verification", "trusted-v8.7.0-src.zip"), ZipArchiveMode.Create);
        for (var i = 0; i < (duplicateEntry ? 2 : 1); i++)
        {
            using var stream = archive.CreateEntry("src/Fixture.cs").Open();
            stream.Write(bytes);
        }
    }

    private bool ReadVerified()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Baseline));
        return json.RootElement.GetProperty("functions")[0].GetProperty("verified").GetBoolean();
    }

    private (string Fingerprint, DateTimeOffset VerifiedAtUtc) ReadFunctionState(string signature)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Baseline));
        foreach (var function in json.RootElement.GetProperty("functions").EnumerateArray())
        {
            var id = function.GetProperty("id").GetString();
            if (id is not null && id.EndsWith("::" + signature, StringComparison.Ordinal))
            {
                return (
                    function.GetProperty("fingerprint").GetString()!,
                    function.GetProperty("verifiedAtUtc").GetDateTimeOffset());
            }
        }
        throw new InvalidOperationException($"Missing function state for {signature}.");
    }

    private void WriteSource(string source) => File.WriteAllText(Path.Combine(root, "src", "Fixture.cs"), source);
    private int Scan() => Verifier.Main(["--root", root, "--mode", "scan"]);
    private int Confirm() => Verifier.Main(["--root", root, "--mode", "confirm"]);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
