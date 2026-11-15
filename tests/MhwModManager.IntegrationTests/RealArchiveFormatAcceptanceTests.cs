using System.Security.Cryptography;
using System.Text;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Pinned real archive fixtures from SharpCompress v0.50.4, rather than synthetic
/// ZIP bytes renamed to RAR/7z. These exercise the pinned production decoder.
/// Upstream fixture identities are pinned and SHA-1 verified; tests use a local corpus
/// if provided, or fetch the upstream public fixtures without credentials.
/// </summary>
public sealed class RealArchiveFormatAcceptanceTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-real-archive-format-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Rar4.rar", 59998, "189b954c16730be8bd06c5e4fc6b0ffe9c9ff87f")]
    [InlineData("Rar5.solid.rar", 60046, "e16a01da81526b480f9d006d73497e1a9656d010")]
    [InlineData("7Zip.nonsolid.7z", 58982, "907e1e2c24542ad2d276756dc7d0ecb1f2665eca")]
    [InlineData("7Zip.solid.7z", 58959, "5ac695e59aeff2385e9065cb8841118f1bde06e1")]
    public async Task Real_rar_and_sevenzip_archives_inspect_and_extract_with_correct_bounded_content(
        string fixture, int expectedBytes, string gitBlobSha)
    {
        var archivePath = await Materialize(fixture, expectedBytes, gitBlobSha);
        var inspector = new ArchiveInspector();
        var inventory = await inspector.InspectAsync(archivePath, TestToken);
        Assert.False(inventory.HasSuspiciousPaths);
        var entries = inventory.Entries.Where(entry => !entry.IsDirectory).ToArray();
        Assert.NotEmpty(entries);

        var output = Path.Combine(home, "extracted", Path.GetFileNameWithoutExtension(fixture));
        await inspector.ExtractSafelyAsync(archivePath, output, home,
            new ArchiveExtractionLimits(16 * 1024 * 1024, 16 * 1024 * 1024), TestToken);

        var actualFiles = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        Assert.Equal(entries.Length, actualFiles.Length);
        foreach (var entry in entries)
        {
            var destination = Path.GetFullPath(Path.Combine(output,
                entry.Key.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar,
                destination, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(destination), $"Missing extracted real archive entry: {entry.Key}");
            Assert.Equal(entry.Size, new FileInfo(destination).Length);
        }
    }

    [Fact]
    public async Task Malformed_real_rar_fixture_must_not_publish_output()
    {
        const string fixture = "Rar.malformed_512byte.rar";
        var archivePath = await Materialize(fixture, 512,
            "914693f8840453420463ef96e01af7040c8e3ff8");
        var output = Path.Combine(home, "malformed-output");
        var inspector = new ArchiveInspector();
        var error = await Record.ExceptionAsync(() =>
            inspector.ExtractSafelyAsync(archivePath, output, home,
                new ArchiveExtractionLimits(16 * 1024 * 1024, 16 * 1024 * 1024), TestToken));

        Assert.NotNull(error);
        Assert.True(!Directory.Exists(output) ||
            Directory.GetFileSystemEntries(output, "*", SearchOption.AllDirectories).Length == 0,
            "Malformed archives must not leave apparent extracted mod files behind.");
        Assert.True(File.Exists(archivePath));
    }

    private static readonly HttpClient FixtureClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 128 * 1024
    };

    private async Task<string> Materialize(string name, int expectedLength, string blobSha)
    {
        // An explicit local fixture corpus works offline. Otherwise fetch only
        // public upstream fixture bytes pinned to the tested SharpCompress tag,
        // and verify the exact immutable Git blob identity before any parsing.
        var localFixtures = Environment.GetEnvironmentVariable("MHW_REAL_ARCHIVE_FIXTURES_ROOT");
        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(localFixtures))
        {
            bytes = await File.ReadAllBytesAsync(Path.Combine(localFixtures, name), TestToken);
        }
        else
        {
            var url = "https://raw.githubusercontent.com/adamhathcock/sharpcompress/0.50.4/" +
                "tests/TestArchives/Archives/" + Uri.EscapeDataString(name);
            try
            {
                bytes = await FixtureClient.GetByteArrayAsync(url, TestToken);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    "Real archive acceptance needs the pinned public SharpCompress fixture or " +
                    "MHW_REAL_ARCHIVE_FIXTURES_ROOT pointing to an independently verified offline corpus.", ex);
            }
        }
        Assert.Equal(expectedLength, bytes.Length);
        var prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        var gitObject = new byte[prefix.Length + bytes.Length];
        Buffer.BlockCopy(prefix, 0, gitObject, 0, prefix.Length);
        Buffer.BlockCopy(bytes, 0, gitObject, prefix.Length, bytes.Length);
        Assert.Equal(blobSha, Convert.ToHexString(SHA1.HashData(gitObject)).ToLowerInvariant());

        Directory.CreateDirectory(home);
        var destination = Path.Combine(home, name);
        await File.WriteAllBytesAsync(destination, bytes, TestToken);
        return destination;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
