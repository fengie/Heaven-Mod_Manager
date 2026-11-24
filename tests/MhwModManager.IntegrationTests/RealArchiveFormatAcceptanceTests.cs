using System.Net;
using System.Security.Cryptography;
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

    // Digest pins calculated from the GitHub blobs at the immutable upstream
    // commit below. No archive bytes are redistributed in this repository.
    private static readonly PinnedFixtureCorpus SharedFixtures = new(FixtureClient,
        new(StringComparer.Ordinal)
        {
            ["Rar4.rar"] = (59998, "9D9C261E50D3A84AB11A3701C7A736057FC970E59CA365885A41F60270C1875E"),
            ["Rar5.solid.rar"] = (60046, "3043D68C5A81F46ADCBB5393632D243944EAD44EB383A677AC82E68AFDDE27CF"),
            ["7Zip.nonsolid.7z"] = (58982, "221DB581CB21336219963ACB1CD9F9F9760BD7AE1DEC112E3C0C68EB1791354D"),
            ["7Zip.solid.7z"] = (58959, "3C336BF14BEF2DC998B548A304C0C07C9C9DA899F90E426A965A5F322E232B53"),
            ["Rar.malformed_512byte.rar"] = (512, "7D915245989596F6CD080151FA350B54F0551797C26681639FBD856D6CC16088")
        });

    private async Task<string> Materialize(string name, int expectedLength)
    {
        var bytes = await SharedFixtures.ReadAsync(name, expectedLength,
            Environment.GetEnvironmentVariable("MHW_REAL_ARCHIVE_FIXTURES_ROOT"), TestToken);

        Directory.CreateDirectory(home);
        var destination = Path.Combine(home, name);
        await File.WriteAllBytesAsync(destination, bytes, TestToken);
        return destination;
    }

    // One verified successful download per unique fixture per test process.
    // A caller's cancellation does not poison the shared cache. Explicit local
    // corpora are validated independently and never fall back to the network.
    private sealed class PinnedFixtureCorpus(
        HttpClient client, Dictionary<string, (int Length, string Sha256)> pins) : IDisposable
    {
        private const string Upstream = "https://raw.githubusercontent.com/adamhathcock/sharpcompress/" +
            "c083c6efd843a844b0c8f7878787360e815be781/tests/TestArchives/Archives/";
        private const int MaxAttempts = 3;
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly Dictionary<string, byte[]> verified = new(StringComparer.Ordinal);

        public async Task<byte[]> ReadAsync(string name, int expectedLength, string? localRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!pins.TryGetValue(name, out var pin) || pin.Length != expectedLength)
                throw new InvalidDataException($"No matching immutable archive fixture pin for {name}.");

            if (!string.IsNullOrWhiteSpace(localRoot))
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(localRoot, name), cancellationToken);
                Validate(name, bytes, pin);
                return bytes;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (verified.TryGetValue(name, out var cached))
                    return cached.ToArray();

                for (var attempt = 1; ; attempt++)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = await client.GetByteArrayAsync(Upstream + Uri.EscapeDataString(name),
                            cancellationToken);
                    }
                    catch (HttpRequestException ex) when (attempt < MaxAttempts && IsTransient(ex.StatusCode))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                        continue;
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new InvalidOperationException(
                            $"Real archive acceptance needs verified fixture {name}. " +
                            "Provide MHW_REAL_ARCHIVE_FIXTURES_ROOT for offline verification.", ex);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    Validate(name, bytes, pin);
                    verified.Add(name, bytes);
                    return bytes.ToArray();
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private static bool IsTransient(HttpStatusCode? status) => status is
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

        private static void Validate(string name, byte[] bytes, (int Length, string Sha256) pin)
        {
            if (bytes.Length != pin.Length ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), pin.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Archive fixture {name} does not match the pinned length and SHA-256.");
        }

        public void Dispose()
        {
            gate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    // Deterministic acquisition regressions use a synthetic three-byte pin;
    // all existing real archive extraction assertions above remain unchanged.
    private const string SampleName = "sample.rar";
    private static PinnedFixtureCorpus CreateTestCorpus(HttpClient client) => new(client,
        new(StringComparer.Ordinal)
        {
            [SampleName] = (3, "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
        });

    private sealed class FixtureResponseHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return response(++Requests);
        }
    }

    private static HttpResponseMessage SuccessfulFixture(string body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body)) };

    [Fact]
    public async Task Fixture_acquisition_retries_transient_failure_and_shares_only_verified_bytes()
    {
        using var handler = new FixtureResponseHandler(request => request <= 2
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : SuccessfulFixture("abc"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var corpus = CreateTestCorpus(client);

        var results = await Task.WhenAll(
            corpus.ReadAsync(SampleName, 3, null, TestToken),
            corpus.ReadAsync(SampleName, 3, null, TestToken),
            corpus.ReadAsync(SampleName, 3, null, TestToken));
        Assert.Equal(3, handler.Requests);
        results[0][0] = 0;
        Assert.Equal("abc"u8.ToArray(), results[1]);
        Assert.Equal("abc"u8.ToArray(), results[2]);
        Assert.Equal("abc"u8.ToArray(), await corpus.ReadAsync(SampleName, 3, null, TestToken));
        Assert.Equal(3, handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task Fixture_acquisition_fails_closed_after_bounded_retries(HttpStatusCode status, int attempts)
    {
        using var handler = new FixtureResponseHandler(_ => new HttpResponseMessage(status));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var corpus = CreateTestCorpus(client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => corpus.ReadAsync(SampleName, 3, null, TestToken));
        Assert.Equal(status, Assert.IsType<HttpRequestException>(error.InnerException).StatusCode);
        Assert.Equal(attempts, handler.Requests);
    }

    [Theory]
    [InlineData("abd")]
    [InlineData("ab")]
    public async Task Fixture_acquisition_never_caches_an_incorrect_hash_or_length(string wrong)
    {
        using var handler = new FixtureResponseHandler(request => SuccessfulFixture(request == 1 ? wrong : "abc"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var corpus = CreateTestCorpus(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => corpus.ReadAsync(SampleName, 3, null, TestToken));
        Assert.Equal("abc"u8.ToArray(), await corpus.ReadAsync(SampleName, 3, null, TestToken));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Explicit_offline_corpus_must_match_the_pin_and_never_downloads()
    {
        using var handler = new FixtureResponseHandler(_ => SuccessfulFixture("abc"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var corpus = CreateTestCorpus(client);
        Directory.CreateDirectory(home);

        await Assert.ThrowsAsync<FileNotFoundException>(() => corpus.ReadAsync(SampleName, 3, home, TestToken));
        var local = Path.Combine(home, SampleName);
        await File.WriteAllBytesAsync(local, "abd"u8.ToArray(), TestToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => corpus.ReadAsync(SampleName, 3, home, TestToken));
        await File.WriteAllBytesAsync(local, "abc"u8.ToArray(), TestToken);
        Assert.Equal("abc"u8.ToArray(), await corpus.ReadAsync(SampleName, 3, home, TestToken));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Cancelled_fixture_acquisition_does_not_poison_subsequent_reads()
    {
        using var handler = new FixtureResponseHandler(_ => SuccessfulFixture("abc"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var corpus = CreateTestCorpus(client);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => corpus.ReadAsync(SampleName, 3, null, cts.Token));
        Assert.Equal(0, handler.Requests);
        Assert.Equal("abc"u8.ToArray(), await corpus.ReadAsync(SampleName, 3, null, TestToken));
        Assert.Equal(1, handler.Requests);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }
}
