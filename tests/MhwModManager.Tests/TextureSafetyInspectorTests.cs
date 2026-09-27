using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class TextureSafetyInspectorTests
{
    [Fact]
    public void Valid_tex_signature_passes()
    {
        var bytes = new byte[16];
        bytes[0]=(byte)'T'; bytes[1]=(byte)'E'; bytes[2]=(byte)'X';
        var result=TextureSafetyInspector.InspectTexHeader(bytes,128);
        Assert.Equal(TextureSafetySeverity.Safe,result.Severity);
    }

    [Fact]
    public void Wrong_magic_is_blocked()
    {
        var bytes = new byte[16];
        bytes[0]=(byte)'D'; bytes[1]=(byte)'D'; bytes[2]=(byte)'S';
        var result=TextureSafetyInspector.InspectTexHeader(bytes,128);
        Assert.Equal(TextureSafetySeverity.Error,result.Severity);
        Assert.Equal("TEX_BAD_MAGIC",result.Code);
    }

    [Fact]
    public void Truncated_tex_is_blocked()
    {
        var bytes = new byte[]{(byte)'T',(byte)'E',(byte)'X'};
        var result=TextureSafetyInspector.InspectTexHeader(bytes,8);
        Assert.Equal(TextureSafetySeverity.Error,result.Severity);
        Assert.Equal("TEX_TRUNCATED",result.Code);
    }
}
