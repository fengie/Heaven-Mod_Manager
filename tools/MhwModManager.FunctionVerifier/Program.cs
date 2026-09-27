using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MhwModManager.FunctionVerifier;

internal static class Program
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var result = Scan(options);
            WriteReport(options.ReportPath, result);
            // An incomplete inventory must not erase checked functions or persist duplicate IDs.
            // Trace gaps are safe to persist as unchecked; parse/identity errors are not.
            if (result.ParseErrors.Count == 0)
                MergeScanState(options.BaselinePath, result);

            Console.WriteLine($"FUNCTION VERIFICATION: {result.Functions.Count} function(s); " +
                              $"known-good={result.KnownGoodCount}; needs-verification={result.NeedsVerificationCount}; " +
                              $"trace-gaps={result.TraceGapCount}; explicit-call-sites={result.TotalExplicitCallSiteCount}; " +
                              $"uncovered-call-sites={result.UncoveredExplicitCallSiteCount}; parse-errors={result.ParseErrors.Count}.");

            foreach (var error in result.ParseErrors)
                Console.Error.WriteLine("PARSE ERROR: " + error);
            foreach (var gap in result.Functions.Where(x => x.TraceRequired && !x.HasRuntimeTrace))
                Console.Error.WriteLine($"TRACE GAP: {gap.Id} ({gap.RelativePath}:{gap.StartLine})");
            foreach (var uncovered in result.Functions.Where(x => !x.CallSitesCovered && x.InvocationCount > 0))
                Console.Error.WriteLine($"CALL-SITE COVERAGE GAP: {uncovered.Id} has {uncovered.InvocationCount} explicit call site(s) outside known-good/traced coverage.");

            if (result.ParseErrors.Count > 0 || result.TraceGapCount > 0 || result.UncoveredExplicitCallSiteCount > 0)
                return 4;

            if (options.Mode.Equals("confirm", StringComparison.OrdinalIgnoreCase))
            {
                Promote(options.BaselinePath, result);
                Console.WriteLine($"PROMOTED: {result.Functions.Count} exact per-function fingerprint(s) marked verified=true.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 5;
        }
    }

    private static ScanResult Scan(Options options)
    {
        var previous = LoadBaseline(options.BaselinePath);
        var trustedFiles = LoadTrustedFiles(options.TrustedFilesPath);
        var trustedFunctions = LoadTrustedFunctions(options.TrustedSourcePath, trustedFiles);
        ValidateBaseline(previous, options.BaselinePath);
        var previousById = previous.Functions
            .Where(x => x.Verified && !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, x => x, StringComparer.Ordinal);

        var srcRoot = Path.Combine(options.Root, "src");
        if (!Directory.Exists(srcRoot))
            throw new DirectoryNotFoundException($"Source directory not found: {srcRoot}");

        var functions = new List<FunctionState>();
        var parseErrors = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !IsGeneratedBuildPath(srcRoot, file))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var relative = NormalizeRelative(options.Root, file);
            var sourceText = File.ReadAllText(file, Encoding.UTF8);
            var fileSha = Sha256(File.ReadAllBytes(file));
            var trustedWholeFile = trustedFiles.Files.TryGetValue(relative, out var trustedSha)
                                   && StringComparer.OrdinalIgnoreCase.Equals(fileSha, trustedSha);

            var tree = CSharpSyntaxTree.ParseText(
                sourceText,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: file,
                encoding: Encoding.UTF8);
            var syntaxRoot = tree.GetRoot();
            foreach (var diagnostic in tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error))
                parseErrors.Add($"{relative}:{diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");

            foreach (var callable in EnumerateCallables(syntaxRoot))
            {
                var id = BuildId(relative, callable);
                var fingerprint = Fingerprint(callable);
                var exactKnownGood = previousById.TryGetValue(id, out var old)
                                     && old.Verified
                                     && StringComparer.OrdinalIgnoreCase.Equals(old.Fingerprint, fingerprint);
                var trustedFunctionKnownGood = !trustedWholeFile
                                               && trustedFunctions.TryGetValue(relative, out var trustedInFile)
                                               && trustedInFile.TryGetValue(id, out var trustedFingerprint)
                                               && StringComparer.OrdinalIgnoreCase.Equals(fingerprint, trustedFingerprint);
                var knownGood = exactKnownGood || trustedWholeFile || trustedFunctionKnownGood;
                var exemptReason = TraceExemption(relative, callable);
                var hasTrace = HasEntryTrace(callable);
                var traceRequired = !knownGood && exemptReason is null;
                var line = callable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                functions.Add(new FunctionState
                {
                    Id = id,
                    RelativePath = relative,
                    Kind = callable.Kind().ToString(),
                    StartLine = line,
                    Fingerprint = fingerprint,
                    KnownGood = knownGood,
                    VerificationBasis = exactKnownGood ? "exact-function-cache" : trustedWholeFile ? "trusted-v8.7.0-file" : trustedFunctionKnownGood ? "trusted-v8.7.0-function" : "changed-or-new",
                    Verified = knownGood,
                    TraceRequired = traceRequired,
                    HasRuntimeTrace = hasTrace,
                    TraceExemption = exemptReason,
                    InvocationCount = CountExplicitCallSites(callable),
                    CallSitesCovered = knownGood || hasTrace || exemptReason is not null
                });
            }
        }

        foreach (var duplicateId in functions.GroupBy(x => x.Id, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key))
            parseErrors.Add($"Duplicate function ID generated for current source: {duplicateId}");

        return new ScanResult
        {
            FormatVersion = FormatVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            SourceVersion = ReadVersion(options.Root),
            Mode = options.Mode,
            KnownGoodCount = functions.Count(x => x.KnownGood),
            NeedsVerificationCount = functions.Count(x => !x.KnownGood),
            TraceGapCount = functions.Count(x => x.TraceRequired && !x.HasRuntimeTrace),
            TotalExplicitCallSiteCount = functions.Sum(x => x.InvocationCount),
            CoveredExplicitCallSiteCount = functions.Where(x => x.CallSitesCovered).Sum(x => x.InvocationCount),
            UncoveredExplicitCallSiteCount = functions.Where(x => !x.CallSitesCovered).Sum(x => x.InvocationCount),
            ParseErrors = parseErrors,
            Functions = functions.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.StartLine).ToList()
        };
    }

    private static IEnumerable<SyntaxNode> EnumerateCallables(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BaseMethodDeclarationSyntax method when HasBody(method):
                    yield return method;
                    break;
                case LocalFunctionStatementSyntax local when local.Body is not null || local.ExpressionBody is not null:
                    yield return local;
                    break;
                case AccessorDeclarationSyntax accessor when accessor.Body is not null || accessor.ExpressionBody is not null:
                    yield return accessor;
                    break;
                case PropertyDeclarationSyntax property when property.ExpressionBody is not null:
                    yield return property;
                    break;
                case IndexerDeclarationSyntax indexer when indexer.ExpressionBody is not null:
                    yield return indexer;
                    break;
            }
        }
    }

    private static bool HasBody(BaseMethodDeclarationSyntax method) => method switch
    {
        MethodDeclarationSyntax m => m.Body is not null || m.ExpressionBody is not null,
        ConstructorDeclarationSyntax c => c.Body is not null || c.ExpressionBody is not null,
        DestructorDeclarationSyntax d => d.Body is not null || d.ExpressionBody is not null,
        OperatorDeclarationSyntax o => o.Body is not null || o.ExpressionBody is not null,
        ConversionOperatorDeclarationSyntax c => c.Body is not null || c.ExpressionBody is not null,
        _ => false
    };

    private static string BuildId(string relativePath, SyntaxNode callable)
    {
        var typePath = string.Join(".", callable.Ancestors().OfType<TypeDeclarationSyntax>()
            .Reverse().Select(x => x.Identifier.ValueText));
        var signature = CallableSignature(callable);
        return $"{relativePath}::{typePath}::{signature}";
    }

    private static string CallableSignature(SyntaxNode callable) => callable switch
    {
        MethodDeclarationSyntax m => $"method:{ExplicitInterface(m.ExplicitInterfaceSpecifier)}{m.Identifier.ValueText}{TypeParameters(m.TypeParameterList)}{Parameters(m.ParameterList)}",
        ConstructorDeclarationSyntax c => $"ctor:{c.Identifier.ValueText}{Parameters(c.ParameterList)}",
        DestructorDeclarationSyntax d => $"dtor:{d.Identifier.ValueText}()",
        OperatorDeclarationSyntax o => $"operator:{o.OperatorToken.ValueText}{Parameters(o.ParameterList)}",
        ConversionOperatorDeclarationSyntax c => $"conversion:{c.ImplicitOrExplicitKeyword.ValueText}:{TypeText(c.Type)}{Parameters(c.ParameterList)}",
        LocalFunctionStatementSyntax l => $"local:{ContainingCallableSignature(l)}>{l.Identifier.ValueText}{TypeParameters(l.TypeParameterList)}{Parameters(l.ParameterList)}",
        AccessorDeclarationSyntax a => $"accessor:{ContainingMemberIdentity(a)}:{a.Keyword.ValueText}",
        PropertyDeclarationSyntax p => $"property:{ExplicitInterface(p.ExplicitInterfaceSpecifier)}{p.Identifier.ValueText}:get",
        IndexerDeclarationSyntax i => $"indexer:{ExplicitInterface(i.ExplicitInterfaceSpecifier)}{Parameters(i.ParameterList)}:get",
        _ => callable.Kind().ToString()
    };

    private static string ContainingCallableSignature(SyntaxNode node)
    {
        var parent = node.Ancestors().FirstOrDefault(x => x is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax);
        return parent is null ? "<root>" : CallableSignature(parent);
    }

    private static string ContainingMemberIdentity(SyntaxNode node)
    {
        var parent = node.Ancestors().FirstOrDefault(x => x is PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax);
        return parent switch
        {
            PropertyDeclarationSyntax p => "property:" + ExplicitInterface(p.ExplicitInterfaceSpecifier) + p.Identifier.ValueText,
            IndexerDeclarationSyntax i => "indexer:" + ExplicitInterface(i.ExplicitInterfaceSpecifier) + Parameters(i.ParameterList),
            EventDeclarationSyntax e => "event:" + ExplicitInterface(e.ExplicitInterfaceSpecifier) + e.Identifier.ValueText,
            _ => "<member>"
        };
    }

    private static string ExplicitInterface(ExplicitInterfaceSpecifierSyntax? specifier) =>
        specifier is null ? string.Empty : string.Concat(specifier.Name.DescendantTokens().Select(x => x.Text)) + ".";

    private static string TypeParameters(TypeParameterListSyntax? list) =>
        list is null ? string.Empty : "<" + string.Join(",", list.Parameters.Select(x => x.Identifier.ValueText)) + ">";

    private static string Parameters(BaseParameterListSyntax list) =>
        "(" + string.Join(",", list.Parameters.Select(ParameterText)) + ")";

    private static string ParameterText(ParameterSyntax parameter)
    {
        var modifiers = string.Join(" ", parameter.Modifiers.Select(x => x.ValueText));
        var type = parameter.Type is null ? "?" : TypeText(parameter.Type);
        return string.IsNullOrWhiteSpace(modifiers) ? type : modifiers + " " + type;
    }

    private static string TypeText(TypeSyntax type) => string.Concat(type.DescendantTokens().Select(x => x.Text));

    private static string Fingerprint(SyntaxNode callable)
    {
        var builder = new StringBuilder();
        foreach (var token in callable.DescendantTokens(descendIntoTrivia: false))
        {
            builder.Append(token.RawKind).Append(':')
                .Append(token.Text.Length).Append(':')
                .Append(token.Text).Append('|');
        }
        return Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static bool HasEntryTrace(SyntaxNode callable)
    {
        BlockSyntax? body = callable switch
        {
            MethodDeclarationSyntax m => m.Body,
            ConstructorDeclarationSyntax c => c.Body,
            DestructorDeclarationSyntax d => d.Body,
            OperatorDeclarationSyntax o => o.Body,
            ConversionOperatorDeclarationSyntax c => c.Body,
            LocalFunctionStatementSyntax l => l.Body,
            AccessorDeclarationSyntax a => a.Body,
            _ => null
        };
        if (body is null || body.Statements.Count == 0) return false;
        // The scope must be acquired unconditionally and live through the whole body.
        // A call hidden in a branch/lambda, or a discarded/undisposed scope, proves nothing.
        return body.Statements[0] switch
        {
            LocalDeclarationStatementSyntax declaration when declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
                => IsTraceDeclaration(declaration.Declaration),
            UsingStatementSyntax statement when body.Statements.Count == 1
                => statement.Declaration is not null
                    ? IsTraceDeclaration(statement.Declaration)
                    : IsBeginMethodInvocation(statement.Expression),
            _ => false
        };
    }

    private static bool IsTraceDeclaration(VariableDeclarationSyntax declaration) =>
        declaration.Variables.Count == 1
        && IsBeginMethodInvocation(declaration.Variables[0].Initializer?.Value);

    private static bool IsBeginMethodInvocation(ExpressionSyntax? expression)
    {
        if (expression is not InvocationExpressionSyntax invocation) return false;
        var name = string.Concat(invocation.Expression.DescendantTokens().Select(x => x.Text));
        return name is "MasterDebugLog.BeginMethod"
            or "MhwModManager.Core.MasterDebugLog.BeginMethod"
            or "global::MhwModManager.Core.MasterDebugLog.BeginMethod";
    }

    private static int CountExplicitCallSites(SyntaxNode callable) =>
        callable.DescendantNodes(node => node is not LocalFunctionStatementSyntax).Count(node => node is InvocationExpressionSyntax
            or ObjectCreationExpressionSyntax
            or ImplicitObjectCreationExpressionSyntax
            or ConstructorInitializerSyntax);

    private static bool IsGeneratedBuildPath(string srcRoot, string file)
    {
        var relative = Path.GetRelativePath(srcRoot, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                      || part.Equals("bin", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TraceExemption(string relativePath, SyntaxNode callable)
    {
        if (!relativePath.Equals("src/MhwModManager.Core/MasterDebugLog.cs", StringComparison.OrdinalIgnoreCase))
            return null;
        var typeNames = callable.Ancestors().OfType<TypeDeclarationSyntax>().Select(x => x.Identifier.ValueText).ToArray();
        return typeNames.Contains("MasterDebugLog", StringComparer.Ordinal)
            ? "trace-infrastructure-recursion-guard"
            : null;
    }


    private static void MergeScanState(string path, ScanResult result)
    {
        var previous = LoadBaseline(path);
        ValidateBaseline(previous, path);
        var previousById = previous.Functions
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, x => x, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var entries = new List<BaselineEntry>(result.Functions.Count);
        foreach (var function in result.Functions)
        {
            var verified = function.KnownGood;
            DateTimeOffset? verifiedAt = null;
            if (verified)
            {
                if (previousById.TryGetValue(function.Id, out var old)
                    && old.Verified
                    && StringComparer.OrdinalIgnoreCase.Equals(old.Fingerprint, function.Fingerprint))
                    verifiedAt = old.VerifiedAtUtc ?? now;
                else
                    verifiedAt = now;
            }
            entries.Add(new BaselineEntry
            {
                Id = function.Id,
                Fingerprint = function.Fingerprint,
                Verified = verified,
                VerifiedAtUtc = verifiedAt,
                VerificationBasis = function.VerificationBasis
            });
        }
        var document = new BaselineDocument
        {
            FormatVersion = FormatVersion,
            SourceVersion = result.SourceVersion,
            VerifiedAtUtc = entries.All(x => x.Verified) ? now : null,
            Functions = entries
        };
        WriteJsonAtomically(path, document);
    }

    private static void Promote(string path, ScanResult result)
    {
        var baseline = new BaselineDocument
        {
            FormatVersion = FormatVersion,
            SourceVersion = result.SourceVersion,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            Functions = result.Functions.Select(x => new BaselineEntry
            {
                Id = x.Id,
                Fingerprint = x.Fingerprint,
                Verified = true,
                VerifiedAtUtc = DateTimeOffset.UtcNow,
                VerificationBasis = "full-release-confirmation"
            }).ToList()
        };
        WriteJsonAtomically(path, baseline);
    }

    private static void ValidateBaseline(BaselineDocument baseline, string path)
    {
        if (baseline.FormatVersion != FormatVersion)
            throw new InvalidDataException($"Unsupported function verification baseline format {baseline.FormatVersion} in {path}. Expected {FormatVersion}.");
        var duplicates = baseline.Functions
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException($"Function verification baseline contains duplicate function IDs. First duplicate: {duplicates[0]}");
    }

    private static BaselineDocument LoadBaseline(string path)
    {
        if (!File.Exists(path)) return new BaselineDocument();
        try
        {
            return JsonSerializer.Deserialize<BaselineDocument>(File.ReadAllText(path), SerializerOptions) ?? new BaselineDocument();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Function verification baseline is invalid JSON: {path}", ex);
        }
    }

    private static TrustedFilesDocument LoadTrustedFiles(string path)
    {
        if (!File.Exists(path)) return new TrustedFilesDocument();
        try
        {
            var document = JsonSerializer.Deserialize<TrustedFilesDocument>(File.ReadAllText(path), SerializerOptions) ?? new TrustedFilesDocument();
            if (document.FormatVersion != FormatVersion)
                throw new InvalidDataException($"Unsupported trusted-source manifest format {document.FormatVersion} in {path}. Expected {FormatVersion}.");
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Trusted source baseline is invalid JSON: {path}", ex);
        }
    }


    private static Dictionary<string, Dictionary<string, string>> LoadTrustedFunctions(string path, TrustedFilesDocument trustedFiles)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            if (trustedFiles.Files.Count > 0)
                throw new InvalidDataException($"Trusted source snapshot is missing: {path}");
            return result;
        }
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries.Where(x => x.FullName.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
                                                         && x.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var relative = entry.FullName.Replace('\\', '/');
            if (!trustedFiles.Files.TryGetValue(relative, out var expectedSha))
                throw new InvalidDataException($"Trusted source snapshot contains an unmanifested source file: {relative}");
            var actualSha = Sha256(bytes);
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualSha, expectedSha))
                throw new InvalidDataException($"Trusted source snapshot hash mismatch for {relative}. Expected {expectedSha}, found {actualSha}.");
            using var sourceReader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sourceText = sourceReader.ReadToEnd();
            var tree = CSharpSyntaxTree.ParseText(
                sourceText,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: relative,
                encoding: Encoding.UTF8);
            var errors = tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidDataException($"Trusted source snapshot contains C# parse errors in {relative}: {errors[0].GetMessage()}");
            var byId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var callable in EnumerateCallables(tree.GetRoot()))
            {
                var id = BuildId(relative, callable);
                if (!byId.TryAdd(id, Fingerprint(callable)))
                    throw new InvalidDataException($"Trusted source snapshot generates a duplicate function ID in {relative}: {id}");
            }
            if (!result.TryAdd(relative, byId))
                throw new InvalidDataException($"Trusted source snapshot contains a duplicate file: {relative}");
        }
        var missing = trustedFiles.Files.Keys
            .Where(x => x.StartsWith("src/", StringComparison.OrdinalIgnoreCase) && x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(x => !result.ContainsKey(x))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Trusted source snapshot is missing {missing.Length} manifested C# file(s). First missing: {missing[0]}");
        return result;
    }

    private static void WriteReport(string path, ScanResult result) => WriteJsonAtomically(path, result);

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, SerializerOptions) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    private static string ReadVersion(string root)
    {
        var path = Path.Combine(root, "VERSION.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
    }

    private static string NormalizeRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class Options
    {
        public required string Root { get; init; }
        public required string Mode { get; init; }
        public required string BaselinePath { get; init; }
        public required string TrustedFilesPath { get; init; }
        public required string TrustedSourcePath { get; init; }
        public required string ReportPath { get; init; }

        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
                if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
                values[args[i][2..]] = args[++i];
            }

            var root = Path.GetFullPath(values.GetValueOrDefault("root") ?? Directory.GetCurrentDirectory());
            var mode = values.GetValueOrDefault("mode") ?? "scan";
            if (!mode.Equals("scan", StringComparison.OrdinalIgnoreCase) && !mode.Equals("confirm", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("--mode must be 'scan' or 'confirm'.");
            return new Options
            {
                Root = root,
                Mode = mode,
                BaselinePath = Path.GetFullPath(values.GetValueOrDefault("baseline") ?? Path.Combine(root, ".verification", "function-status.json")),
                TrustedFilesPath = Path.GetFullPath(values.GetValueOrDefault("trusted-files") ?? Path.Combine(root, ".verification", "trusted-v8.7.0-files.json")),
                TrustedSourcePath = Path.GetFullPath(values.GetValueOrDefault("trusted-source") ?? Path.Combine(root, ".verification", "trusted-v8.7.0-src.zip")),
                ReportPath = Path.GetFullPath(values.GetValueOrDefault("report") ?? Path.Combine(root, "BuildLogs", "function-verification.json"))
            };
        }
    }

    private sealed class BaselineDocument
    {
        public int FormatVersion { get; init; } = Program.FormatVersion;
        public string? SourceVersion { get; init; }
        public DateTimeOffset? VerifiedAtUtc { get; init; }
        public List<BaselineEntry> Functions { get; init; } = [];
    }

    private sealed class BaselineEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Fingerprint { get; init; } = string.Empty;
        public bool Verified { get; init; }
        public DateTimeOffset? VerifiedAtUtc { get; init; }
        public string? VerificationBasis { get; init; }
    }

    private sealed class TrustedFilesDocument
    {
        public int FormatVersion { get; init; } = Program.FormatVersion;
        public string? SourceVersion { get; init; }
        public string? Basis { get; init; }
        public Dictionary<string, string> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FunctionState
    {
        public string Id { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public int StartLine { get; init; }
        public string Fingerprint { get; init; } = string.Empty;
        public bool KnownGood { get; init; }
        public bool Verified { get; init; }
        public string VerificationBasis { get; init; } = string.Empty;
        public bool TraceRequired { get; init; }
        public bool HasRuntimeTrace { get; init; }
        public string? TraceExemption { get; init; }
        public int InvocationCount { get; init; }
        public bool CallSitesCovered { get; init; }
    }

    private sealed class ScanResult
    {
        public int FormatVersion { get; init; }
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public string SourceVersion { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public int KnownGoodCount { get; init; }
        public int NeedsVerificationCount { get; init; }
        public int TraceGapCount { get; init; }
        public int TotalExplicitCallSiteCount { get; init; }
        public int CoveredExplicitCallSiteCount { get; init; }
        public int UncoveredExplicitCallSiteCount { get; init; }
        public List<string> ParseErrors { get; init; } = [];
        public List<FunctionState> Functions { get; init; } = [];
    }
}
