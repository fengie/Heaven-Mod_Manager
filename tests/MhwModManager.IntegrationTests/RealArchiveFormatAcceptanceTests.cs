using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Pinned real archive fixtures from SharpCompress v0.50.4, rather than synthetic
/// ZIP bytes renamed to RAR/7z. These exercise the pinned production decoder.
/// Fixtures come from a specific immutable upstream source commit rather than
/// a movable tag; tests use a local corpus or public HTTPS without credentials.
/// </summary>
public sealed class RealArchiveFormatAcceptanceTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(),
        "umm-real-archive-format-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Rar4.rar", 59998)]
    [InlineData("Rar5.solid.rar", 60046)]
    [InlineData("7Zip.nonsolid.7z", 58982)]
    [InlineData("7Zip.solid.7z", 58959)]
    public async Task Real_rar_and_sevenzip_archives_inspect_and_extract_with_correct_bounded_content(
        string fixture, int expectedBytes)
    {
        var archivePath = await Materialize(fixture, expectedBytes);
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
    public async Task SolidSevenZipPreservesEachMemberAcrossDecoderBoundaries()
    {
        var input = await Materialize("7Zip.solid.7z", 58959);
        var output = Path.Combine(home, "verified-bytes");
        await new ArchiveInspector().ExtractSafelyAsync(input, output, home, TestToken);
        // Independently extracted with 7-Zip from the pinned upstream archive.
        var hashes = new Dictionary<string, string>
        {
            [@"exe\test.exe"] = "8557928804F57ECC340B3BB38B095A3607474EC8DEB0076F316FCFE02B562106",
            [@"jpg\test.jpg"] = "B251C7501FB0F55DD4A92FEABE0A6F5733BC40A02679498155FAE9B30138FC53",
            ["\u0442\u0435\u0441\u0442.txt"] = "4D581D93D369F6E1C9B295FF38D82DABD577F927DFAF0C35818C015C85E322D9"
        };
        Assert.Equal(hashes.Count,Directory.GetFiles(output,"*",SearchOption.AllDirectories).Length);
        foreach (var (relative, hash) in hashes)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(output,relative),TestToken);
            Assert.Equal(hash,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        }
    }

    [Fact]
    public async Task SolidSevenZipStopsAtActualBudgetAndCleansOnlyItsPartialFile()
    {
        var input = await Materialize("7Zip.solid.7z",58959);
        var original = await File.ReadAllBytesAsync(input,TestToken);
        var output = Directory.CreateDirectory(Path.Combine(home,"limited")).FullName;
        var sentinel = Path.Combine(output,"keep.txt");
        await File.WriteAllTextAsync(sentinel,"owned",TestToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ArchiveInspector().ExtractSafelyAsync(
            input,output,home,new ArchiveExtractionLimits(1024*1024,1),TestToken));
        Assert.Equal(sentinel,Assert.Single(Directory.GetFiles(output,"*",SearchOption.AllDirectories)));
        Assert.Equal("owned",await File.ReadAllTextAsync(sentinel,TestToken));
        Assert.Equal(original,await File.ReadAllBytesAsync(input,TestToken));
    }

    [Fact]
    public async Task Malformed_real_rar_fixture_must_not_publish_output()
    {
        const string fixture = "Rar.malformed_512byte.rar";
        var archivePath = await Materialize(fixture, 512);
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


    // Unlike renamed ZIP fixtures, these are actual RAR4, solid RAR5 and 7z bytes.
    // Progress must reflect what was really extracted, not a synthetic percentage.
    [Theory]
    [InlineData("Rar4.rar", 59998)]
    [InlineData("Rar5.solid.rar", 60046)]
    [InlineData("7Zip.nonsolid.7z", 58982)]
    [InlineData("7Zip.solid.7z", 58959)]
    public async Task RealArchiveFormatsReportExactExtractionCountsAndBytes(string fixture, int expectedBytes)
    {
        var archivePath = await Materialize(fixture, expectedBytes);
        var originalBytes = await File.ReadAllBytesAsync(archivePath, TestToken);
        var inspector = new ArchiveInspector();
        var inventory = await inspector.InspectAsync(archivePath, TestToken);
        var expectedFiles = inventory.Entries.Count(entry => !entry.IsDirectory);
        var expectedPayloadBytes = inventory.Entries
            .Where(entry => !entry.IsDirectory).Sum(entry => entry.Size);
        var events = new List<ArchiveExtractionProgress>();
        var target = Path.Combine(home, "progress", Path.GetFileNameWithoutExtension(fixture));

        await inspector.ExtractSafelyAsync(archivePath, target, home,
            new ArchiveExtractionLimits(16 * 1024 * 1024, 16 * 1024 * 1024),
            new ImmediateProgress<ArchiveExtractionProgress>(events.Add), TestToken);

        Assert.NotEmpty(events);
        Assert.Equal(0, events[0].CompletedFiles);
        Assert.Equal(0, events[0].CompletedBytes);
        Assert.All(events, progress =>
        {
            Assert.Equal(expectedFiles, progress.TotalFiles);
            Assert.Equal(expectedPayloadBytes, progress.TotalBytes);
            Assert.InRange(progress.CompletedFiles, 0, expectedFiles);
            Assert.InRange(progress.CompletedBytes, 0L, expectedPayloadBytes);
        });
        for (var i = 1; i < events.Count; i++)
        {
            Assert.True(events[i].CompletedFiles >= events[i - 1].CompletedFiles,
                "Reported completed-file count must never go backwards.");
            Assert.True(events[i].CompletedBytes >= events[i - 1].CompletedBytes,
                "Reported completed-byte count must never go backwards.");
        }
        Assert.Equal(expectedFiles, events[^1].CompletedFiles);
        Assert.Equal(expectedPayloadBytes, events[^1].CompletedBytes);
        Assert.Equal(expectedFiles, Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(archivePath, TestToken));
    }

    [Theory]
    [InlineData("Rar4.rar", 59998)]
    [InlineData("Rar5.solid.rar", 60046)]
    [InlineData("7Zip.nonsolid.7z", 58982)]
    [InlineData("7Zip.solid.7z", 58959)]
    public async Task RealArchiveCancellationBeforeFirstPayloadPreservesSourceAndDoesNotPublish(
        string fixture, int expectedBytes)
    {
        var archivePath = await Materialize(fixture, expectedBytes);
        var originalBytes = await File.ReadAllBytesAsync(archivePath, TestToken);
        var target = Path.Combine(home, "cancelled", Path.GetFileNameWithoutExtension(fixture));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        var progress = new ImmediateProgress<ArchiveExtractionProgress>(state =>
        {
            if (state.CompletedFiles == 0 && state.CompletedBytes == 0) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArchiveInspector().ExtractSafelyAsync(archivePath, target, home,
                new ArchiveExtractionLimits(16 * 1024 * 1024, 16 * 1024 * 1024),
                progress, cts.Token));

        Assert.True(!Directory.Exists(target) ||
            Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length == 0,
            "A cancelled archive must not look like an extracted installable package.");
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(archivePath, TestToken));
    }

    [Theory]
    [InlineData("Rar4.rar", 59998)]
    [InlineData("Rar5.solid.rar", 60046)]
    [InlineData("7Zip.nonsolid.7z", 58982)]
    [InlineData("7Zip.solid.7z", 58959)]
    public async Task RealArchiveSizeBudgetRejectsBeforeFirstFileIsWritten(string fixture, int expectedBytes)
    {
        var archivePath = await Materialize(fixture, expectedBytes);
        var sourceBytes = await File.ReadAllBytesAsync(archivePath, TestToken);
        var target = Path.Combine(home, "over-budget", Path.GetFileNameWithoutExtension(fixture));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ArchiveInspector().ExtractSafelyAsync(archivePath, target, home,
                new ArchiveExtractionLimits(1, 16 * 1024 * 1024), TestToken));

        Assert.False(Directory.Exists(target),
            "Declared archive budget must be enforced before any destination is created.");
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(archivePath, TestToken));
    }

    private sealed class ImmediateProgress<T>(Action<T> receive) : IProgress<T>
    {
        public void Report(T value) => receive(value);
    }

    private static readonly HttpClient FixtureClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 128 * 1024
    };

    private async Task<string> Materialize(string name, int expectedLength)
    {
        // An explicit local fixture corpus works offline. Otherwise fetch only
        // public upstream fixture bytes pinned to the tested SharpCompress tag,
        // and check fixed fixture lengths before any parsing.
        var localFixtures = Environment.GetEnvironmentVariable("MHW_REAL_ARCHIVE_FIXTURES_ROOT");
        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(localFixtures))
        {
            bytes = await File.ReadAllBytesAsync(Path.Combine(localFixtures, name), TestToken);
        }
        else
        {
            var url = "https://raw.githubusercontent.com/adamhathcock/sharpcompress/c083c6efd843a844b0c8f7878787360e815be781/" +
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
