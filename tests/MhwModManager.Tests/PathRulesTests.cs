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
