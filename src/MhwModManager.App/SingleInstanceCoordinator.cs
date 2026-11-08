using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using MhwModManager.Core;

namespace MhwModManager.App;

/// <summary>
/// One interactive mod-manager owner per Windows user and desktop session. The mutex
/// is an existence sentinel, not an acquired synchronization lock: holding its handle
/// for the application's lifetime avoids thread-affine ReleaseMutex problems on WPF.
/// The only allowed IPC message is a fixed, bounded same-user ACTIVATE request.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly byte[] ActivationRequest = Encoding.ASCII.GetBytes("ACTIVATE\n");
    private static readonly byte[] ActivationAcknowledgement = Encoding.ASCII.GetBytes("OK\n");
    private readonly Mutex instanceMarker;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private Task? listenTask;
    private bool disposed;

    public bool IsPrimary { get; }

    public static string ProductInstanceKey
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            var sid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Windows identity unavailable for same-user instance ownership.");
            // Pipe names have no Local\\/Global\\ namespace. Include the interactive
            // Windows session ID explicitly to prevent cross-desktop activation.
            using var process = Process.GetCurrentProcess();
            return "Heaven-Mod-Manager|" + sid + "|session=" + process.SessionId;
        }
    }

    public SingleInstanceCoordinator(string key)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var identity = "Heaven-Mod-Manager-" + digest;
        instanceMarker = new Mutex(initiallyOwned: false, "Local\\" + identity, out var createdNew);
        IsPrimary = createdNew;
        pipeName = identity;
    }

    public void StartListening(Action onActivate)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(onActivate);
        if (!IsPrimary) throw new InvalidOperationException("Only the primary mod-manager instance can accept activation.");
        if (listenTask is not null || disposed) throw new InvalidOperationException("The activation listener was already started or disposed.");
        listenTask = Task.Run(() => ListenAsync(onActivate, stop.Token));
    }

    public async Task<bool> RequestActivationAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (IsPrimary) return false;
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(6))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
                await client.WriteAsync(ActivationRequest, deadline.Token).ConfigureAwait(false);
                await client.FlushAsync(deadline.Token).ConfigureAwait(false);
                var response = new byte[ActivationAcknowledgement.Length];
                await client.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
                return response.AsSpan().SequenceEqual(ActivationAcknowledgement);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                MasterDebugLog.Write("SINGLE-INSTANCE", "Primary activation is not available yet.", ex);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(150), ct).ConfigureAwait(false);
        }
        return false;
    }

    private async Task ListenAsync(Action onActivate, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                // Read a fixed number of bytes; ReadLineAsync would buffer unbounded
                // untrusted text before the later length check.
                var request = new byte[ActivationRequest.Length];
                await server.ReadExactlyAsync(request, deadline.Token).ConfigureAwait(false);
                if (!request.AsSpan().SequenceEqual(ActivationRequest))
                    continue;
                onActivate();
                await server.WriteAsync(ActivationAcknowledgement, deadline.Token).ConfigureAwait(false);
                await server.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { /* Bound a stalled same-user client. */ }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                MasterDebugLog.Write("SINGLE-INSTANCE", "Activation channel reset after recoverable error.", ex);
                try { await Task.Delay(200, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        instanceMarker.Dispose();
        // The listener observes cancellation and closes its owned pipe asynchronously.
        // No blocking wait is permitted on the WPF UI shutdown thread.
        GC.SuppressFinalize(this);
    }
}
