using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using MhwModManager.Core;

namespace MhwModManager.Filesystem;

public sealed record LocalPluginIdentity(string Id, string Title, string Version, string Sha256);

public static class BepInExPluginMetadata
{
    public static IReadOnlyList<LocalPluginIdentity> Read(string path, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is 0 or > 128 * 1024 * 1024) return [];
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) return [];
            var metadata = pe.GetMetadataReader();
            var plugins = new List<(string Id, string Title, string Version)>();
            foreach (var type in metadata.TypeDefinitions)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var handle in metadata.GetTypeDefinition(type).GetCustomAttributes())
                {
                    var attribute = metadata.GetCustomAttribute(handle);
                    if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                    var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    if (constructor.Parent.Kind != HandleKind.TypeReference || !metadata.StringComparer.Equals(constructor.Name, ".ctor")) continue;
                    var owner = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                    if (!metadata.StringComparer.Equals(owner.Namespace, "BepInEx") ||
                        !metadata.StringComparer.Equals(owner.Name, "BepInPlugin") ||
                        owner.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
                    var assembly = metadata.GetAssemblyReference((AssemblyReferenceHandle)owner.ResolutionScope);
                    if (metadata.GetString(assembly.Name) is not ("BepInEx" or "BepInEx.Core")) continue;
                    // Instance .ctor(string, string, string), returning void.
                    if (!metadata.GetBlobBytes(constructor.Signature).AsSpan().SequenceEqual(new byte[] { 0x20, 3, 1, 0x0e, 0x0e, 0x0e })) continue;
                    var data = metadata.GetBlobReader(attribute.Value);
                    if (data.Length > 16 * 1024 || data.ReadUInt16() != 1) continue;
                    var id = data.ReadSerializedString();
                    var title = data.ReadSerializedString();
                    var version = data.ReadSerializedString();
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(version) ||
                        data.RemainingBytes != 2 || data.ReadUInt16() != 0) continue;
                    plugins.Add((id, title, version));
                }
            }
            if (plugins.Count == 0) return [];
            stream.Position = 0;
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            return plugins.Distinct().Select(p => new LocalPluginIdentity(p.Id, p.Title, p.Version, digest)).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return [];
        }
    }
}

public static partial class LocalModInventory
{
    public static IReadOnlyList<ExternalLibraryReference> RegisteredPackageLibrary(IEnumerable<LocalModLocation> inventory,
        GameProfile profile, IEnumerable<string> managedSources, CancellationToken ct = default) =>
        RegisteredPackageLibrary(inventory,profile,managedSources,null,null,ct);

    public static IReadOnlyList<ExternalLibraryReference> RegisteredPackageLibrary(IEnumerable<LocalModLocation> inventory,
        GameProfile profile, IEnumerable<string> managedSources, IReadOnlyDictionary<string,string>? sourceTitles,
        Dictionary<string,string>? ownedLocations, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var plugins = new List<(LocalPluginIdentity Plugin, string Path, long Length)>();
        var effects = new List<(ExternalLibraryReference Row, string DllHash, string ConfigHash, long Length)>();
        var pluginRoot = Path.GetFullPath(Path.Combine(profile.GameRoot, "BepInEx", "plugins")) + Path.DirectorySeparatorChar;
        foreach (var entry in inventory.Where(row => row.RegisteredProfileId == profile.Id))
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.Location.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(entry.Location) ||
                HasLinkedAncestor(entry.Location)) continue;
            if (entry.Location.StartsWith(pluginRoot, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var plugin in BepInExPluginMetadata.Read(entry.Location, ct))
                    plugins.Add((plugin, entry.Location, new FileInfo(entry.Location).Length));
            }
            else if (entry.Evidence.EndsWith("ReShade injector and configuration", StringComparison.Ordinal))
            {
                var dir = Path.GetDirectoryName(entry.Location)!;
                var relative = Path.GetRelativePath(profile.GameRoot, dir).Split(Path.DirectorySeparatorChar);
                var projectRuntime = relative.Length == 3 && relative[1].Equals("Binaries", StringComparison.OrdinalIgnoreCase)
                    && relative[2].Equals("Win64", StringComparison.OrdinalIgnoreCase) && Directory.EnumerateFiles(dir,"*.exe").Any();
                if (!PathRules.Comparer.Equals(dir, profile.GameRoot) &&
                    !PathRules.Comparer.Equals(dir, Path.GetDirectoryName(profile.ExecutablePath)) && !projectRuntime) continue;
                var config = Path.Combine(dir, "ReShade.ini");
                var dllHash = LocalFileDigest(entry.Location);
                var configHash = LocalFileDigest(config);
                if (dllHash is not null && configHash is not null)
                    effects.Add((new("ReShade", "ReShade", "Installed injector and configuration", [entry.Location,config]),
                        dllHash, configHash, new FileInfo(entry.Location).Length));
            }
        }

        if (plugins.Count == 0 && effects.Count == 0) return [];
        var lengths = plugins.Select(p => p.Length).Concat(effects.Select(e => e.Length)).ToHashSet();
        var captured = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var capturedEffects = new Dictionary<(string,string),string>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true, MaxRecursionDepth = 64 };
        var remainingEntries = 300_000;
        var scannedPaths = new HashSet<string>(PathRules.Comparer);
        foreach (var source in managedSources.Distinct(PathRules.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(source) || HasLinkedAncestor(source)) continue;
            foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (--remainingEntries < 0) throw new IOException("Managed package comparison exceeded the inventory scan limit.");
                if (!file.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                    !lengths.Contains(file.Length) || !scannedPaths.Add(file.FullName)) continue;
                var digest = LocalFileDigest(file.FullName);
                if (digest is null) continue;
                // Equal bytes include the complete assembly's attribute, even for disabled packages without an index.
                var title = sourceTitles?.GetValueOrDefault(source) ?? Path.GetFileName(source);
                captured.TryAdd(digest,title);
                if (effects.Any(e => e.DllHash == digest) && LocalFileDigest(Path.Combine(file.DirectoryName!, "ReShade.ini")) is { } config)
                    capturedEffects.TryAdd((digest,config),title);
            }
        }

        if (ownedLocations != null)
        {
            foreach (var plugin in plugins)
                if (captured.TryGetValue(plugin.Plugin.Sha256,out var title)) ownedLocations.TryAdd(plugin.Path,title);
            foreach (var effect in effects)
                if (capturedEffects.TryGetValue((effect.DllHash,effect.ConfigHash),out var title))
                    foreach (var path in effect.Row.Locations) ownedLocations.TryAdd(path,title);
        }
        var rows = plugins.Where(p => !captured.ContainsKey(p.Plugin.Sha256)).GroupBy(p => p.Plugin)
            .Select(g => new ExternalLibraryReference(g.Key.Id, g.Key.Title,
                $"BepInEx · {g.Key.Id} · {g.Key.Version} · {g.Key.Sha256[..12]}",
                g.Select(p => p.Path).Distinct(PathRules.Comparer).OrderBy(p => p, PathRules.Comparer).ToArray()));
        return rows.Concat(effects.Where(e => !capturedEffects.ContainsKey((e.DllHash,e.ConfigHash))).Select(e => e.Row))
            .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.OwnerLabel, StringComparer.Ordinal).ToArray();
    }

    private static string? LocalFileDigest(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if (!File.Exists(path) || HasLinkedAncestor(path)) return null;
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length > 128 * 1024 * 1024 ? null : Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
