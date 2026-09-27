using System.Diagnostics;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

/// <summary>
/// Best-effort preview resolver. Package screenshots/adjacent images work with no dependencies.
/// For raw MHW .tex conflicts, optional external MHW TEX + DirectXTex converters can be configured
/// through MHW_TEX_CONVERTER and TEXCONV_EXE to generate cached PNG previews.
/// </summary>
public sealed class TexturePreviewService(string cacheRoot)
{
    private static readonly string[] PreviewExtensions = [".png",".jpg",".jpeg",".bmp"];
    public async Task<string?> GetPreviewAsync(ModDescriptor mod,string managedPath,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={mod.Id}; managedPath={managedPath}");
        if(!string.IsNullOrWhiteSpace(mod.PreviewPath)&&File.Exists(mod.PreviewPath))return mod.PreviewPath;
        var source=ResolveSourceFile(mod.SourcePath,managedPath);if(source is null)return null;
        var adjacent=FindAdjacentImage(source);if(adjacent is not null)return adjacent;
        if(!source.EndsWith(".tex",StringComparison.OrdinalIgnoreCase))return null;
        try{return await TryConvertTexAsync(source,ct);}
        catch(OperationCanceledException){throw;}
        catch(IOException){return null;}
        catch(UnauthorizedAccessException){return null;}
        catch(InvalidOperationException){return null;}
    }

    private async Task<string?> TryConvertTexAsync(string source,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var texConverter=Environment.GetEnvironmentVariable("MHW_TEX_CONVERTER");
        var texconv=Environment.GetEnvironmentVariable("TEXCONV_EXE");
        if(string.IsNullOrWhiteSpace(texConverter)||!File.Exists(texConverter)||string.IsNullOrWhiteSpace(texconv)||!File.Exists(texconv))return null;
        Directory.CreateDirectory(cacheRoot);
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source+File.GetLastWriteTimeUtc(source).Ticks))).ToLowerInvariant();
        var outPng=Path.Combine(cacheRoot,key+".png");if(File.Exists(outPng))return outPng;
        var work=Path.Combine(cacheRoot,key);Directory.CreateDirectory(work);var copied=Path.Combine(work,Path.GetFileName(source));File.Copy(source,copied,true);
        var before=Directory.EnumerateFiles(work,"*.dds").ToHashSet(StringComparer.OrdinalIgnoreCase);
        await RunAsync(texConverter,$"\"{copied}\"",work,ct);
        var dds=Directory.EnumerateFiles(work,"*.dds").FirstOrDefault(f=>!before.Contains(f));if(dds is null)return null;
        await RunAsync(texconv,$"-y -ft png -o \"{cacheRoot}\" \"{dds}\"",work,ct);
        var generated=Path.Combine(cacheRoot,Path.GetFileNameWithoutExtension(dds)+".png");
        if(!File.Exists(generated))return null;File.Move(generated,outPng,true);return outPng;
    }

    private static async Task RunAsync(string exe,string args,string working,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        using var p=ProcessDebug.Start(new ProcessStartInfo(exe,args){WorkingDirectory=working,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=false,RedirectStandardError=false}, "texture-preview-converter");
        await p.WaitForExitAsync(ct);if(p.ExitCode!=0)throw new InvalidOperationException($"Preview converter exited with code {p.ExitCode}.");
    }

    private static string? ResolveSourceFile(string root,string managedPath)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var normalized=PathRules.Normalize(managedPath);
        if(normalized.StartsWith("nativePC\\",StringComparison.OrdinalIgnoreCase))
        {
            var rel=normalized["nativePC\\".Length..];var p1=Path.Combine(root,"nativePC",rel);if(File.Exists(p1))return p1;var p2=Path.Combine(root,rel);if(File.Exists(p2))return p2;
        }
        if(normalized.StartsWith("root\\",StringComparison.OrdinalIgnoreCase)){var rel=normalized["root\\".Length..];var p=Path.Combine(root,"GameRoot",rel);if(File.Exists(p))return p;}
        return null;
    }

    private static string? FindAdjacentImage(string source)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dir=Path.GetDirectoryName(source)!;var stem=Path.GetFileNameWithoutExtension(source);
        foreach(var ext in PreviewExtensions){var p=Path.Combine(dir,stem+ext);if(File.Exists(p))return p;}
        return null;
    }
}
