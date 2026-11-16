using BenchmarkDotNet.Attributes;
using MhwModManager.Core;

namespace MhwModManager.Benchmarks;

/// <summary>
/// Different filesystem topologies can change the amount of family inference
/// work even at the same mod/file count. Compare independent roots to a
/// common four-segment root without manufacturing overlapping payloads.
/// </summary>
[MemoryDiagnoser]
public class FamilyTopologyBenchmarks
{
    [Params(100, 500)]
    public int ModCount { get; set; }

    [Params("IndependentRoots", "SharedRoot")]
    public string Topology { get; set; } = "IndependentRoots";

    private ModDescriptor[] mods = [];
    private ModFileDescriptor[] files = [];

    [GlobalSetup]
    public void Setup()
    {
        var descriptors = new List<ModDescriptor>(ModCount);
        var inventory = new List<ModFileDescriptor>(ModCount * 20);
        var now = DateTimeOffset.UnixEpoch;

        for (var index = 0; index < ModCount; index++)
        {
            var id = $"mod-{index:D5}";
            descriptors.Add(new(id, $"Synthetic {index:D5}",
                $"Synthetic {index:D5}", id, false, index));
            var prefix = Topology == "SharedRoot"
                ? @"nativePC\shared\collection\common"
                : $@"nativePC\shared\collection\mod{index:D5}";
            for (var file = 0; file < 20; file++)
            {
                var path = $@"{prefix}\mod{index:D5}\file{file:D3}.tex";
                inventory.Add(new ModFileDescriptor(id, path,
                    $"{index:X8}{file:X8}".PadRight(64, '0'),
                    null, 1024, now, FileClass.Texture));
            }
        }

        mods = descriptors.ToArray();
        files = inventory.ToArray();
    }

    [Benchmark]
    public int InferFamilies()
    {
        var inferred = GenericFamilyInference.InferKeys(mods, files, out var evidence);
        if (inferred.Count != 0 || evidence.Count != 0)
            throw new InvalidDataException(
                $"Unrelated synthetic mods were grouped: {inferred.Count} keys / {evidence.Count} links");
        return inferred.Count;
    }
}
