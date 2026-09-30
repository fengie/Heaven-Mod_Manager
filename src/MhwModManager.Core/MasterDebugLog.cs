using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;

namespace MhwModManager.Core;

/// <summary>
/// Dependency-free, best-effort master trace used by every layer of the manager.
/// This logger must never be allowed to become a product failure.
/// </summary>
public static class MasterDebugLog
{
    private static readonly object Gate = new();
    private static readonly AsyncLocal<string?> CurrentOperation = new();
    private static readonly AsyncLocal<OperationScope?> CurrentScope = new();
    private static string rootDirectory = ResolveInitialRoot();
    private static StreamWriter? writer;
    private static string? writerPath;
    private static int hooksInstalled;
    private static long totalFirstChanceExceptions;
    private static readonly bool FirstChanceDetailEnabled = IsEnabled(Environment.GetEnvironmentVariable("MHW_FIRST_CHANCE_DETAIL"));
    private static readonly bool MethodTraceDetailEnabled =
        FirstChanceDetailEnabled
        || IsEnabled(Environment.GetEnvironmentVariable("MHW_METHOD_TRACE_DETAIL"))
        || IsEnabled(Environment.GetEnvironmentVariable("MOD_MANAGER_DIAGNOSTIC"))
        || IsEnabled(Environment.GetEnvironmentVariable("MHWMM_DIAGNOSTIC"));
    [ThreadStatic] private static bool writing;
    [ThreadStatic] private static bool handlingFirstChance;

    public static string RootDirectory
    {
        get { lock (Gate) return rootDirectory; }
    }

    public static string FilePath => Path.Combine(RootDirectory, "MHW-DEBUG-ALL.log");
    public static string? CurrentOperationId => CurrentOperation.Value;
    internal static bool IsWriting => writing;

    public static void Configure(string? preferredRoot)
    {
        if (string.IsNullOrWhiteSpace(preferredRoot)) return;
        try
        {
            var newRoot = Path.GetFullPath(preferredRoot);
            Directory.CreateDirectory(newRoot);
            string? previousPath = null;
            lock (Gate)
            {
                if (!string.Equals(rootDirectory, newRoot, StringComparison.OrdinalIgnoreCase))
                {
                    previousPath = Path.Combine(rootDirectory, "MHW-DEBUG-ALL.log");
                    CloseWriterLocked();
                    rootDirectory = newRoot;
                }
            }

            var targetPath = Path.Combine(newRoot, "MHW-DEBUG-ALL.log");
            if (!string.IsNullOrWhiteSpace(previousPath)
                && !string.Equals(previousPath, targetPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(previousPath))
            {
                try
                {
                    var earlier = File.ReadAllText(previousPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(earlier)) File.AppendAllText(targetPath, earlier, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            Write("MASTER", $"Master debug root configured: {newRoot}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    public static void InstallGlobalExceptionHooks()
    {
        if (Interlocked.Exchange(ref hooksInstalled, 1) != 0) return;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("UNHANDLED-APPDOMAIN-GLOBAL", $"IsTerminating={args.IsTerminating}", args.ExceptionObject as Exception);
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            Write("ASSEMBLY", $"Loaded {args.LoadedAssembly.FullName}; location={SafeAssemblyLocation(args.LoadedAssembly)}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Write("PROCESS", $"Current Universal Mod Manager process exiting. pid={Environment.ProcessId}; exitCode={Environment.ExitCode}; firstChanceExceptions={Interlocked.Read(ref totalFirstChanceExceptions)}");
            lock (Gate) CloseWriterLocked();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Write("UNOBSERVED-TASK-GLOBAL", "TaskScheduler.UnobservedTaskException", args.Exception);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(x => x.GetName().Name, StringComparer.OrdinalIgnoreCase))
            Write("ASSEMBLY", $"Already loaded {assembly.FullName}; location={SafeAssemblyLocation(assembly)}");
        Write("MASTER", $"Global hooks installed: FirstChanceException, UnhandledException, UnobservedTaskException, AssemblyLoad, ProcessExit. firstChanceDetail={FirstChanceDetailEnabled}; methodTraceDetail={MethodTraceDetailEnabled}");
    }

    public static OperationScope Begin(
        string area,
        string operation,
        string? detail = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        return new OperationScope(area, operation, detail, caller, sourceFile, sourceLine, verbose: true);
    }

    public static OperationScope BeginMethod(
        string? detail = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        var component = Path.GetFileNameWithoutExtension(sourceFile);
        return new OperationScope("METHOD", component + "." + member, detail, member, sourceFile, sourceLine, MethodTraceDetailEnabled);
    }

    public static async Task TraceAsync(string area, string operation, Func<Task> action, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var scope = Begin(area, operation, detail);
        try { await action().ConfigureAwait(false); scope.Success(); }
        catch (Exception ex) { scope.Fail(ex); throw; }
    }

    public static async Task<T> TraceAsync<T>(string area, string operation, Func<Task<T>> action, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var scope = Begin(area, operation, detail);
        try { var result = await action().ConfigureAwait(false); scope.Success(); return result; }
        catch (Exception ex) { scope.Fail(ex); throw; }
    }

    public static void Section(string area, string title)
    {
        Write(area, new string('=', 96));
        Write(area, title);
        Write(area, new string('=', 96));
    }

    public static void Write(string area, string message, Exception? exception = null)
    {
        if (writing) return;
        try
        {
            writing = true;
            var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            var operation = CurrentOperation.Value;
            var prefix = $" [pid={Environment.ProcessId}] [tid={Environment.CurrentManagedThreadId}]" + (string.IsNullOrWhiteSpace(operation) ? string.Empty : $" [op={operation}]");
            var normalized = (message ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
            var builder = new StringBuilder();
            foreach (var line in normalized.Split('\n'))
                builder.Append(now).Append(" [").Append(area).Append(']').Append(prefix).Append(' ').AppendLine(line);
            if (exception is not null)
            {
                foreach (var line in exception.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                    builder.Append(now).Append(" [").Append(area).Append(']').Append(prefix).Append(" EXCEPTION ").AppendLine(line);
            }

            lock (Gate)
            {
                EnsureWriterLocked().Write(builder.ToString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            lock (Gate) CloseWriterLocked();
        }
        finally { writing = false; }
    }

    private static string SafeAssemblyLocation(System.Reflection.Assembly assembly)
    {
        try { return string.IsNullOrWhiteSpace(assembly.Location) ? "<dynamic-or-bundled>" : assembly.Location; }
        catch (Exception) { return "<unavailable>"; }
    }

    private static StreamWriter EnsureWriterLocked()
    {
        var path = Path.Combine(rootDirectory, "MHW-DEBUG-ALL.log");
        if (writer is not null && string.Equals(writerPath, path, StringComparison.OrdinalIgnoreCase)) return writer;
        CloseWriterLocked();
        Directory.CreateDirectory(rootDirectory);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024) { AutoFlush = true };
        writerPath = path;
        return writer;
    }

    private static void CloseWriterLocked()
    {
        try { writer?.Flush(); } catch (Exception) { }
        try { writer?.Dispose(); } catch (Exception) { }
        writer = null;
        writerPath = null;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (writing || handlingFirstChance) return;
        handlingFirstChance = true;
        try
        {
            Interlocked.Increment(ref totalFirstChanceExceptions);
            var ex = args.Exception;
            MarkExceptionObserved(ex);
            if (FirstChanceDetailEnabled)
                Write("FIRST-CHANCE", $"{ex.GetType().FullName}: {ex.Message}", ex);
        }
        catch
        {
            // A diagnostic hook must never become a second product failure. The thread-static
            // recursion guard is already set before any work so exceptions here cannot recurse.
        }
        finally
        {
            handlingFirstChance = false;
        }
    }

    private static void MarkExceptionObserved(Exception exception)
    {
        for (var scope = CurrentScope.Value; scope is not null; scope = scope.Parent)
            scope.ObserveException(exception);
    }

    private static bool IsEnabled(string? value) =>
        value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("on", StringComparison.OrdinalIgnoreCase));

    private static string ResolveInitialRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MHW_MASTER_DEBUG_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) configured = Environment.GetEnvironmentVariable("MHW_MANAGER_HOME");
        if (string.IsNullOrWhiteSpace(configured)) configured = AppContext.BaseDirectory;
        try { return Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }

    public sealed class OperationScope : IDisposable
    {
        private readonly string area;
        private readonly string operation;
        private string? id;
        private readonly string? parentId;
        private readonly string? previousOperation;
        private readonly OperationScope? previousScope;
        private readonly long startedTimestamp;
        private readonly bool verbose;
        private int outcome;
        private int disposed;
        private int observedExceptionCount;
        private string? firstObservedExceptionType;
        private string? firstObservedExceptionMessage;

        internal OperationScope(string area, string operation, string? detail, string caller, string sourceFile, int sourceLine, bool verbose)
        {
            this.area = area;
            this.operation = operation;
            this.verbose = verbose;
            startedTimestamp = Stopwatch.GetTimestamp();
            parentId = CurrentOperation.Value;
            previousOperation = CurrentOperation.Value;
            previousScope = CurrentScope.Value;
            CurrentScope.Value = this;
            if (verbose)
            {
                id = NewTraceId();
                CurrentOperation.Value = id;
                var location = $"{Path.GetFileName(sourceFile)}:{sourceLine}";
                Write(area, $"START {operation}; id={id}; parent={parentId ?? "<none>"}; caller={caller}; source={location}{FormatDetail(detail)}");
            }
        }

        public void Success(string? detail = null)
        {
            if (Interlocked.CompareExchange(ref outcome, 1, 0) != 0) return;
            var errors = Volatile.Read(ref observedExceptionCount);
            if (verbose || errors != 0)
            {
                var status = errors == 0 ? "PASS" : "PASS-WITH-ERROR-CHECK";
                Write(area, $"{status} {operation}; id={TraceId}; elapsedMs={ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}; {FormatObservedErrors(errors)}{FormatDetail(detail)}");
            }
        }

        public void Fail(Exception exception, string? detail = null)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (Interlocked.CompareExchange(ref outcome, 2, 0) != 0) return;
            var errors = Volatile.Read(ref observedExceptionCount);
            Write(area, $"FAIL {operation}; id={TraceId}; elapsedMs={ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}; {FormatObservedErrors(errors)}{FormatDetail(detail)}", exception);
        }

        internal void ObserveException(Exception exception)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            Interlocked.Increment(ref observedExceptionCount);
            var type = exception.GetType().FullName ?? exception.GetType().Name;
            if (Interlocked.CompareExchange(ref firstObservedExceptionType, type, null) is null)
                Volatile.Write(ref firstObservedExceptionMessage, exception.Message);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (Volatile.Read(ref outcome) == 0)
            {
                var errors = Volatile.Read(ref observedExceptionCount);
                if (errors == 0)
                {
                    if (verbose)
                        Write(area, $"PASS-CHECK {operation}; id={TraceId}; elapsedMs={ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}; observedExceptions=0");
                }
                else
                {
                    Write(area, $"ERROR-CHECK {operation}; id={TraceId}; elapsedMs={ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}; {FormatObservedErrors(errors)}");
                }
            }
            if (verbose) CurrentOperation.Value = previousOperation;
            CurrentScope.Value = previousScope;
            GC.SuppressFinalize(this);
        }

        internal OperationScope? Parent => previousScope;
        private string TraceId => id ??= NewTraceId();
        private double ElapsedMilliseconds => Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        private static string NewTraceId() => Guid.NewGuid().ToString("N")[..12];

        private string FormatObservedErrors(int errors) => errors == 0
            ? "observedExceptions=0"
            : $"observedExceptions={errors}; firstException={firstObservedExceptionType ?? "<unknown>"}; firstMessage={Volatile.Read(ref firstObservedExceptionMessage) ?? "<none>"}; note=exception-may-have-been-handled";

        private static string FormatDetail(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : "; detail=" + detail;
    }
}

public static class ProcessDebug
{
    public static Process Start(ProcessStartInfo startInfo, string purpose)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"purpose={purpose}");
        ArgumentNullException.ThrowIfNull(startInfo);
        var safeArguments = RedactArguments(startInfo.Arguments);
        MasterDebugLog.Write("PROCESS", $"START purpose={purpose}; file={startInfo.FileName}; args={safeArguments}; workingDirectory={startInfo.WorkingDirectory}; shell={startInfo.UseShellExecute}");
        try
        {
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Process.Start returned null for '{startInfo.FileName}'.");
            MasterDebugLog.Write("PROCESS", $"STARTED purpose={purpose}; pid={process.Id}; file={startInfo.FileName}");
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    try { MasterDebugLog.Write("PROCESS", $"EXIT purpose={purpose}; pid={process.Id}; exitCode={process.ExitCode}"); }
                    catch (Exception ex) { MasterDebugLog.Write("PROCESS", $"EXIT purpose={purpose}; pid={process.Id}; exitCode=<unavailable>", ex); }
                };
            }
            catch (Exception ex)
            {
                MasterDebugLog.Write("PROCESS", $"Could not attach exit observer. purpose={purpose}; pid={process.Id}", ex);
            }
            return process;
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write("PROCESS", $"FAIL purpose={purpose}; file={startInfo.FileName}", ex);
            throw;
        }
    }

    private static string RedactArguments(string arguments)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(arguments)) return string.Empty;
        var text = arguments;
        foreach (var marker in new[] { "--api-key", "--apikey", "--token", "--password", "-Password" })
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var valueStart = index + marker.Length;
            var valueEnd = text.IndexOf(' ', valueStart + 1);
            if (valueEnd < 0) valueEnd = text.Length;
            text = text[..valueStart] + " <redacted>" + text[valueEnd..];
        }
        return text;
    }
}
