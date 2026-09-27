using MhwModManager.Automation;
using Xunit;

namespace MhwModManager.AutomationTests;

public sealed class AutomationLogicTests
{
    [Fact]
    public void CategoryClassifierRecognizesArmorAndPlugin()
    {
        Assert.Equal(AutomationCategory.Armor, AutoCategoryService.ClassifyPaths([@"nativePC\pl\f_equip\pl001_0000\f_body.mod3", @"nativePC\pl\f_equip\pl001_0000\f_body.mrl3"]));
        Assert.Equal(AutomationCategory.Plugin, AutoCategoryService.ClassifyPaths([@"nativePC\plugins\example.dll"]));
    }


    [Fact]
    public void CategoryClassifierIgnoresRootLevelDocumentationAndUnsafePaths()
    {
        string[] paths = [
            "Troubleshootings.txt",
            "README.md",
            @"..\escape.dll",
            @"nativePC\pl\f_equip\pl001_0000\f_body.mod3"
        ];

        Assert.Equal(AutomationCategory.Armor, AutoCategoryService.ClassifyPaths(paths));
    }

    [Fact]
    public void CategoryClassifierReturnsUnknownForDocumentationOnly()
    {
        string[] paths = ["Troubleshootings.txt", "README.md"];
        Assert.Equal(AutomationCategory.Unknown, AutoCategoryService.ClassifyPaths(paths));
    }

    [Fact]
    public async Task CrashBisectorIsolatesSingleCulprit()
    {
        var engine=new CrashBisectorEngine();
        var result=await engine.RunAsync(["a","b","c","d"],(enabled,_)=>Task.FromResult(enabled.Contains("c")),TestContext.Current.CancellationToken);
        Assert.True(result.Isolated); Assert.Single(result.Suspects); Assert.Equal("c",result.Suspects[0]);
    }

    [Fact]
    public async Task CrashBisectorReportsInteractionWhenHalvesAreClean()
    {
        var engine=new CrashBisectorEngine();
        var result=await engine.RunAsync(["a","b"],(enabled,_)=>Task.FromResult(enabled.Contains("a")&&enabled.Contains("b")),TestContext.Current.CancellationToken);
        Assert.False(result.Isolated); Assert.Equal(2,result.Suspects.Count);
    }
}
