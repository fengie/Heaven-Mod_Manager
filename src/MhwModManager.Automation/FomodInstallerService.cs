using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MhwModManager.Core;

namespace MhwModManager.Automation;

public sealed record FomodOption(string Id, string Name, string Description, string Type, bool Selected);
public sealed record FomodGroup(string Name, string Type, IReadOnlyList<FomodOption> Options);
public sealed record FomodStep(string Name, bool Visible, IReadOnlyList<FomodGroup> Groups);
public sealed record FomodCopy(string Source, string Destination, int Priority);
public sealed record FomodSelection(string ConfigSha256, IReadOnlyList<string> SelectedOptions);

/// <summary>Choice-aware XML installer. Unknown dependency types fail closed; no scripts are executed.</summary>
public sealed class FomodInstallerService
{
    private readonly string root;
    private readonly XElement config;
    public string ConfigSha256 { get; }
    public string Name { get; }

    public FomodInstallerService(string packageRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        root = Path.GetFullPath(packageRoot);
        var configs = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(p => Path.GetFileName(p).Equals("ModuleConfig.xml", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "fomod", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (configs.Length != 1) throw new InvalidDataException("Expected exactly one fomod/ModuleConfig.xml.");
        root = Path.GetDirectoryName(Path.GetDirectoryName(configs[0]))!;
        EnsureNoLinks(root, configs[0]);
        using var stream = File.OpenRead(configs[0]);
        ConfigSha256 = Convert.ToHexString(SHA256.HashData(stream)); stream.Position = 0;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        config = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty FOMOD config.");
        if (config.Name != "config") throw new InvalidDataException("Unsupported FOMOD XML namespace or root.");
        Name = (string?)config.Element("moduleName") ?? Path.GetFileName(root);
        var known = new HashSet<string>(StringComparer.Ordinal) { "moduleName", "moduleImage", "moduleDependencies", "requiredInstallFiles", "installSteps", "conditionalFileInstalls" };
        if (config.Elements().Any(e => !known.Contains(e.Name.LocalName))) throw new InvalidDataException("This installer contains unsupported top-level elements.");
    }

    public static bool HasInstaller(string packageRoot)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Directory.EnumerateFiles(packageRoot, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Any(p => Path.GetFileName(p).Equals("ModuleConfig.xml", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "fomod", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<FomodStep> Describe(IReadOnlySet<string> selected)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Evaluate(config.Element("moduleDependencies"), flags)) throw new InvalidDataException("Installer module dependencies are not satisfied.");
        var result = new List<FomodStep>();
        var si = 0;
        foreach (var step in Ordered(config.Element("installSteps"), "installStep"))
        {
            var visible = Evaluate(step.Element("visible"), flags);
            var groups = new List<FomodGroup>(); var gi = 0;
            foreach (var group in Ordered(step.Element("optionalFileGroups"), "group"))
            {
                var groupType = Required(group, "type");
                if (groupType is not ("SelectExactlyOne" or "SelectAtMostOne" or "SelectAtLeastOne" or "SelectAny" or "SelectAll")) throw new InvalidDataException("Unsupported group type: " + groupType);
                var options = new List<FomodOption>(); var pi = 0;
                foreach (var plugin in Ordered(group.Element("plugins"), "plugin"))
                {
                    var id = $"{si}/{gi}/{pi++}"; var type = PluginType(plugin, flags);
                    var chosen = selected.Contains(id) || type == "Required" || groupType == "SelectAll";
                    options.Add(new(id, Required(plugin, "name"), (string?)plugin.Element("description") ?? "", type, chosen));
                }
                groups.Add(new(Required(group, "name"), groupType, options)); gi++;
            }
            result.Add(new(Required(step, "name"), visible, groups));
            // Flags from a step become available to later steps, not sibling choices in the same step.
            if (visible)
            {
                gi = 0;
                foreach (var group in Ordered(step.Element("optionalFileGroups"), "group"))
                {
                    var pi = 0;
                    foreach (var plugin in Ordered(group.Element("plugins"), "plugin"))
                    {
                        if (groups[gi].Options[pi++].Selected)
                            foreach (var flag in plugin.Element("conditionFlags")?.Elements("flag") ?? []) flags[Required(flag, "name")] = flag.Value;
                    }
                    gi++;
                }
            }
            si++;
        }
        return result;
    }

    public IReadOnlyList<FomodCopy> Plan(IReadOnlySet<string> selected, GameProfile game)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var described = Describe(selected);
        var knownIds = described.SelectMany(s => s.Groups).SelectMany(g => g.Options).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(id => !knownIds.Contains(id))) throw new InvalidDataException("Saved selections do not match this installer.");
        var directives = new List<XElement>(config.Element("requiredInstallFiles")?.Elements() ?? []);
        var flags = new Dictionary<string, string>(StringComparer.Ordinal); var si = 0;
        foreach (var step in Ordered(config.Element("installSteps"), "installStep"))
        {
            var description = described[si++];
            if (!description.Visible) continue;
            var gi = 0;
            foreach (var group in Ordered(step.Element("optionalFileGroups"), "group"))
            {
                var choices = description.Groups[gi++]; var count = choices.Options.Count(o => o.Selected);
                if ((choices.Type == "SelectExactlyOne" && count != 1) || (choices.Type == "SelectAtMostOne" && count > 1) || (choices.Type == "SelectAtLeastOne" && count == 0))
                    throw new InvalidDataException($"{choices.Name}: selection does not satisfy {choices.Type}.");
                var pi = 0;
                foreach (var plugin in Ordered(group.Element("plugins"), "plugin"))
                {
                    var option = choices.Options[pi++];
                    if (option.Selected && option.Type == "NotUsable") throw new InvalidDataException(option.Name + " is not usable.");
                    foreach (var file in plugin.Element("files")?.Elements() ?? [])
                        if (option.Selected || Bool(file, "alwaysInstall") || (Bool(file, "installIfUsable") && option.Type != "NotUsable")) directives.Add(file);
                    if (option.Selected)
                        foreach (var flag in plugin.Element("conditionFlags")?.Elements("flag") ?? []) flags[Required(flag, "name")] = flag.Value;
                }
            }
        }
        foreach (var pattern in config.Element("conditionalFileInstalls")?.Element("patterns")?.Elements("pattern") ?? [])
            if (Evaluate(pattern.Element("dependencies"), flags)) directives.AddRange(pattern.Element("files")?.Elements() ?? []);
        var copies = new Dictionary<string, FomodCopy>(StringComparer.OrdinalIgnoreCase);
        foreach (var directive in directives)
        {
            if (directive.Name.LocalName is not ("file" or "folder")) throw new InvalidDataException("Unsupported install directive.");
            var relativeSource = Required(directive, "source"); var source = SafePath(root, relativeSource);
            var destination = (string?)directive.Attribute("destination") ?? (directive.Name.LocalName == "file" ? relativeSource : "");
            var priority = (int?)directive.Attribute("priority") ?? 0;
            IEnumerable<string> files = directive.Name.LocalName == "folder" ? EnumeratePayload(source) : [source];
            foreach (var file in files)
            {
                EnsureNoLinks(root, file);
                if (!File.Exists(file)) throw new FileNotFoundException("Installer source file is missing.", file);
                var relative = directive.Name.LocalName == "folder" ? Path.Combine(destination.Replace('\\', Path.DirectorySeparatorChar), Path.GetRelativePath(source, file)) : destination;
                relative = relative.Replace('\\', '/');
                if (game.IsMonsterHunterWorld)
                {
                    if (relative.StartsWith("root/", StringComparison.OrdinalIgnoreCase)) relative = "GameRoot/" + relative[5..];
                    else if (!relative.StartsWith("nativePC/", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("GameRoot/", StringComparison.OrdinalIgnoreCase)) relative = "nativePC/" + relative;
                }
                if (!PathRules.IsSafeArchiveRelativePath(relative)) throw new InvalidDataException("Unsafe installer destination: " + relative);
                var copy = new FomodCopy(file, relative, priority);
                if (!copies.TryGetValue(relative, out var prior) || priority > prior.Priority) copies[relative] = copy;
                else if (priority == prior.Priority && !StringComparer.OrdinalIgnoreCase.Equals(prior.Source, file)) throw new InvalidDataException("Two selections write the same destination at equal priority: " + relative);
            }
        }
        if (copies.Count == 0) throw new InvalidDataException("The selected installer options contain no files.");
        return copies.Values.OrderBy(c => c.Destination, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task InstallAsync(IReadOnlySet<string> selected, GameProfile game, string destination, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var plan = Plan(selected, game);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Installer destination must be new.");
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var copy in plan)
            {
                ct.ThrowIfCancellationRequested(); EnsureNoLinks(root, copy.Source);
                var target = SafePath(destination, copy.Destination); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = File.OpenRead(copy.Source); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
        }
        catch { Directory.Delete(destination, true); throw; }
    }

    public FomodSelection Remember(IReadOnlySet<string> selected)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return new(ConfigSha256, selected.Order(StringComparer.Ordinal).ToArray());
    }

    private static string PluginType(XElement plugin, Dictionary<string, string> flags)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var descriptor = plugin.Element("typeDescriptor");
        var type = (string?)descriptor?.Element("type")?.Attribute("name");
        var dependent = descriptor?.Element("dependencyType");
        if (dependent is not null)
        {
            type = (string?)dependent.Element("defaultType")?.Attribute("name");
            foreach (var pattern in dependent.Element("patterns")?.Elements("pattern") ?? [])
                if (Evaluate(pattern.Element("dependencies"), flags)) { type = (string?)pattern.Element("type")?.Attribute("name"); break; }
        }
        if (type is not ("Required" or "Optional" or "Recommended" or "NotUsable" or "CouldBeUsable")) throw new InvalidDataException("Unknown plugin type: " + type);
        return type;
    }
    private static bool Evaluate(XElement? expression, Dictionary<string, string> flags)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (expression is null) return true;
        if (expression.Name.LocalName == "flagDependency") return flags.GetValueOrDefault(Required(expression, "flag"), "") == Required(expression, "value");
        if (expression.Name.LocalName is not ("dependencies" or "moduleDependencies" or "visible")) throw new InvalidDataException("Unsupported installer dependency: " + expression.Name + ". Install manually or use a compatible installer.");
        var op = (string?)expression.Attribute("operator") ?? "And";
        if (op is not ("And" or "Or")) throw new InvalidDataException("Unknown dependency operator.");
        var values = expression.Elements().Select(e => Evaluate(e, flags)).ToArray(); // Evaluate all: unsupported dependencies must never silently disappear through short-circuiting.
        return op == "And" ? values.All(x => x) : values.Any(x => x);
    }
    private static IEnumerable<XElement> Ordered(XElement? parent, string child)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var elements = parent?.Elements(child) ?? [];
        return (string?)parent?.Attribute("order") switch
        {
            "Ascending" => elements.OrderBy(e => (string?)e.Attribute("name"), StringComparer.OrdinalIgnoreCase),
            "Descending" => elements.OrderByDescending(e => (string?)e.Attribute("name"), StringComparer.OrdinalIgnoreCase),
            _ => elements
        };
    }
    private static bool Bool(XElement element, string attribute)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (bool?)element.Attribute(attribute) ?? false;
    }
    private static string Required(XElement element, string attribute)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var value = (string?)element.Attribute(attribute);
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException("Missing " + attribute + " on " + element.Name) : value;
    }
    private static IEnumerable<string> EnumeratePayload(string directory)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stack = new Stack<string>(); stack.Push(directory);
        while (stack.Count > 0)
        {
            var current = stack.Pop(); EnsureNoLinks(directory, current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installer payload contains a symbolic link or reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) stack.Push(entry); else yield return entry;
            }
        }
    }
    private static string SafePath(string directory, string relative)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!PathRules.IsSafeArchiveRelativePath(relative)) throw new InvalidDataException("Unsafe installer path: " + relative);
        return Path.GetFullPath(Path.Combine(directory, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
    }
    private static void EnsureNoLinks(string directory, string file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var current = new FileInfo(file) as FileSystemInfo;
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installer paths cannot contain symbolic links or reparse points.");
            if (StringComparer.OrdinalIgnoreCase.Equals(current.FullName, directory)) break;
            current = current is FileInfo info ? info.Directory : ((DirectoryInfo)current).Parent;
        }
    }
}
