using MhwModManager.Core;using Xunit;
namespace MhwModManager.Tests;
public sealed class RuleGraphTests
{
    private static ConflictRule O(string id,string loser,string winner)=>new(id,RuleKind.Overlay,RuleScope.ModPair,loser,winner,winner,null,"test",true,DateTimeOffset.UtcNow);
    [Fact] public void Finds_cycle(){var cycle=RuleGraph.FindCycle([O("1","a","b"),O("2","b","c"),O("3","c","a")]);Assert.NotNull(cycle);Assert.True(cycle!.Count>=4);}
    [Fact] public void Allows_dag(){Assert.Null(RuleGraph.FindCycle([O("1","a","b"),O("2","b","c")]));}
}
