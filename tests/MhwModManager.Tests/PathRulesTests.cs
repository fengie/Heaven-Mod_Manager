using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class PathRulesTests
{
    [Theory]
    [InlineData(@"nativePC\..\evil.dll")]
    [InlineData(@"nativePC\CON\evil.dll")]
    [InlineData(@"nativePC\folder\name. ")]
    [InlineData(@"nativePC\folder\stream:ads")]
    [InlineData(@"\\server\share\evil.dll")]
    public void Unsafe_windows_paths_are_rejected(string path) =>
        Assert.Throws<ArgumentException>(() => PathRules.Normalize(path));

    [Theory]
    [InlineData(@"..\evil.dll")]
    [InlineData(@"C:\evil.dll")]
    [InlineData(@"CON\evil.dll")]
    [InlineData(@"folder\stream:ads")]
    public void Unsafe_archive_paths_are_rejected(string path) =>
        Assert.False(PathRules.IsSafeArchiveRelativePath(path));

    [Theory]
    [InlineData(@"folder\NUL.tar.gz")]
    [InlineData(@"folder\con.backup.bin")]
    [InlineData(@"folder\COM¹.txt")]
    [InlineData(@"LPT²\data.bin")]
    [InlineData(@"folder\LPT³.skin.tex")]
    [InlineData(@"folder\name ")]
    [InlineData(@"folder\bad?name.tex")]
    [InlineData("folder\\bad\u0001name.tex")]
    public void Windows_unsafe_names_are_rejected_independently_of_test_host(string path)
    {
        Assert.False(PathRules.IsSafeArchiveRelativePath(path));
        Assert.Throws<ArgumentException>(() => PathRules.Normalize(@"nativePC\" + path));
    }

    [Theory]
    [InlineData(@"folder\COM10.tex")]
    [InlineData(@"folder\console.tar.gz")]
    [InlineData(@"folder\.metadata")]
    [InlineData(@"folder\衣装.tex")]
    public void Ordinary_nondevice_names_remain_supported(string path)
    {
        Assert.True(PathRules.IsSafeArchiveRelativePath(path));
        Assert.Equal(@"nativePC\" + path, PathRules.Normalize(@"nativePC\" + path));
    }

    [Fact]
    public void Archive_directory_marker_does_not_hide_an_unsafe_name()
    {
        Assert.True(PathRules.IsSafeArchiveRelativePath("ordinary/"));
        Assert.False(PathRules.IsSafeArchiveRelativePath("NUL.tar.gz/"));
        Assert.False(PathRules.IsSafeArchiveRelativePath("name /"));
    }

    [Fact]
    public void Armor_component_is_indexed_without_string_search()
    {
        Assert.True(PathRules.TryGetArmorComponent(
            @"nativePC\pl\f_equip\pl032_0010\wst\mod\f_wst032_0010.mod3",
            out var model,
            out var component));
        Assert.Equal("pl032_0010", model);
        Assert.Equal("waist", component);
    }
}
