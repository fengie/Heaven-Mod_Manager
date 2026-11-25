using BenchmarkDotNet.Attributes;
using MhwModManager.Core;

namespace MhwModManager.Benchmarks;

[MemoryDiagnoser]
public class TextureRelationshipBenchmarks
{
    [Params(100, 500)]
    public int ModCount { get; set; }

    private ModDescriptor[] mods = [];
    private ModFileDescriptor[] files = [];

    [GlobalSetup]
    public void Setup()
    {
        var packages = new List<ModDescriptor>();
        var inventory = new List<ModFileDescriptor>();
        for (var i = 0; i < ModCount / 2; i++)
        {
            var parent = $"base-{i}";
            var child = $"overlay-{i}";
            var name = $"Body Set {i:D4}";
            packages.Add(new(parent, name + " Base", name + " Base", parent, false, 0));
            packages.Add(new(child, name + " Texture Overlay", name + " Texture Overlay", child, false, 0));
            var root = $@"nativePC\pl\f_equip\pl{i:D3}_0000\body";
            inventory.Add(new(parent, root + @"\body.mod3", new string('c', 64), null,
                4096, DateTimeOffset.UnixEpoch, PathRules.ClassifyFile(root + @"\body.mod3")));
            for (var j = 0; j < 256; j++)
            {
                var path = $@"{root}\detail{j}.tex";
                inventory.Add(new(parent, path, new string('a', 64), null, 4096,
                    DateTimeOffset.UnixEpoch, FileClass.Texture));
                inventory.Add(new(child, path, new string('b', 64), null, 4096,
                    DateTimeOffset.UnixEpoch, FileClass.Texture));
            }
        }
        mods = packages.ToArray();
        files = inventory.ToArray();
    }

    [Benchmark]
    public int SuggestLargeTextureOverlays()
    {
        var suggestions = ModRelationshipInference.Suggest(mods, files, ModCount);
        if (suggestions.Count != ModCount / 2)
            throw new InvalidDataException($"Expected {ModCount / 2} edges, found {suggestions.Count}.");
        return suggestions.Count;
    }
}
