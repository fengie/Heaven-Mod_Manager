using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

/// <summary>Pair inference must give the same authority and winner in the indexed planner path.</summary>
public sealed class OverlayRuleCacheParityTests
{
    [Fact]
    public void Generated_overlay_matches_direct_inference_for_mixed_case_mod_ids()
    {
        var main = new ModDescriptor("BASE", "HPN Sexy Kulu-Ya-Ku",
            "HPN Sexy Kulu-Ya-Ku", "base", true, 0);
        var option = new ModDescriptor("WaIsT", "HPN Sexy Kulu-Ya-Ku - Skimpy Waist",
            "HPN Sexy Kulu-Ya-Ku - Skimpy Waist", "waist", true, 1);
        var pair = ConflictEngine.PairKey(main.Id, option.Id);
        var overlap = new PairStats(100, 20, 20);

        var inferred = AutoCompatibility.TryInferOverlay(main, option, overlap,
            out var parent, out var child, out _, out var confidence);

        Assert.True(inferred);
        Assert.Equal(Confidence.High, confidence);
        Assert.Equal(main.Id, parent.Id);
        Assert.Equal(option.Id, child.Id);

        var mods = new Dictionary<string, ModDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [main.Id] = main,
            [option.Id] = option
        };
        var stats = new Dictionary<(string,string), PairStats> { [pair] = overlap };
        var rule = Assert.Single(AutoCompatibility.GenerateOverlayRules(mods, stats, []));

        Assert.Equal(main.Id, rule.LeftModId);
        Assert.Equal(option.Id, rule.RightModId);
        Assert.Equal(child.Id, rule.WinnerModId);
        Assert.False(rule.Explicit);
    }
}
