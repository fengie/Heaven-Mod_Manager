using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class NexusArchiveIdentityTests
{
    [Theory]
    [InlineData("tattoo-skin-texture-overlay-8397-1-0-1763815951-500733aabbeeccdd00112233445566778899aabbccddeeff0011223344556677", "8397", "tattoo-skin-texture-overlay")]
    [InlineData("tattoo-skin-texture-overlay-8411-1-0-1766850951-54ed6e10c979c084eb88664a3b639870e0b5f2e2a25ea270bb2f3fc042c22", "8411", "tattoo-skin-texture-overlay")]
    [InlineData("Knight Commission Armor - Top-1965-3-10-0", "1965", "Knight Commission Armor - Top")]
    [InlineData("Knight Commission Armor - Top-1965-3-10-0.zip", "1965", "Knight Commission Armor - Top")]
    [InlineData("PawnCompanion 1377 1.35 2026-06-24T03-45Z G8alq8bQH.zip", "1377", "PawnCompanion")]
    [InlineData("wst-fix-8556-1-2026-07-24t11-22z-ksakzfaie", "8556", "wst-fix")]
    [InlineData("wst-fix-8556-1-2026-07-24T11-22Z-ksakzfaie.rar", "8556", "wst-fix")]
    [InlineData("GiftOfSpellCraft 0.0.1 184672 1 2026-07-07T17-09Z GkqKVMPSF.zip", "184672", "GiftOfSpellCraft 0.0.1")]
    public void Recognizes_archive_identity_and_keeps_distinct_mod_pages(string archiveName, string expectedModId, string expectedDisplay)
    {
        Assert.True(NexusArchiveIdentity.TryParse(archiveName, out var identity));
        Assert.Equal(expectedModId, identity.ModId);
        Assert.Equal(expectedDisplay, identity.DisplayName);
        Assert.Equal(expectedDisplay, NexusArchiveIdentity.RemoveSuffix(archiveName));
    }

    [Theory]
    [InlineData("texture-overlay-8397-1-0-1763815951-not-a-verified-hex-digest")]
    [InlineData("Texture Overlay v1.0")]
    [InlineData("PawnCompanion 1377 1.35 2026-13-40T03-45Z G8alq8bQH.zip")]
    [InlineData("wst-fix-8556-1-2026-13-40t11-22z-ksakzfaie")]
    [InlineData("wst-fix-8556-1-2026-07-24t11-22z-bad!")]
    [InlineData("PawnCompanion 1377 1.35 2026-06-24T03-45Z bad!.zip")]
    [InlineData("NormalFolder-123-456-notnexus")]
    [InlineData("unrelated-1-0-1763815951-54ed6e10c979c084eb88664a3b639870")]
    public void Does_not_invent_identity_from_unrecognized_names(string folder)
    {
        Assert.False(NexusArchiveIdentity.TryParse(folder, out _));
        Assert.Equal(folder, NexusArchiveIdentity.RemoveSuffix(folder));
    }

    [Fact]
    public void Modern_iso_format_retains_a_file_specific_slug_as_non_authoritative_provenance()
    {
        Assert.True(NexusArchiveIdentity.TryParse(
            "PawnCompanion 1377 1.35 2026-06-24T03-45Z G8alq8bQH.zip", out var identity));
        Assert.Equal("1377", identity.ModId);
        Assert.Equal("G8alq8bQH", identity.FileSlug);
    }

    [Fact]
    public void Hyphen_delimited_mod_download_retains_file_slug_only_as_unverified_provenance()
    {
        Assert.True(NexusArchiveIdentity.TryParse(
            "wst-fix-8556-1-2026-07-24t11-22z-ksakzfaie", out var identity));

        Assert.Equal("8556", identity.ModId);
        Assert.Equal("wst-fix", identity.DisplayName);
        Assert.Equal("ksakzfaie", identity.FileSlug);
    }

    [Fact]
    public void Different_archive_source_pages_remain_independent_logical_mods()
    {
        const string first = "tattoo-skin-texture-overlay-8397-1-0-1763815951-500733aabbeeccdd00112233445566778899aabbccddeeff0011223344556677";
        const string second = "tattoo-skin-texture-overlay-8411-1-0-1766850951-54ed6e10c979c084eb88664a3b639870e0b5f2e2a25ea270bb2f3fc042c22";
        Assert.True(NexusArchiveIdentity.TryParse(first, out var a));
        Assert.True(NexusArchiveIdentity.TryParse(second, out var b));

        var mods = new[]
        {
            new ModDescriptor("a", first, a.DisplayName, first, false, 1, NexusModId: a.ModId),
            new ModDescriptor("b", second, b.DisplayName, second, false, 2, NexusModId: b.ModId)
        };

        Assert.Equal(2, LogicalModFamilies.Build(mods).Count);
    }
}
