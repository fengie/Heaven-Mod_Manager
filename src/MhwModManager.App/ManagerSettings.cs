using System.IO;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class ManagerSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool UiAnimationsEnabled { get; set; } = true;
    public bool RememberLastTab { get; set; } = true;
    public int LastSelectedTab { get; set; }
    public bool ConfirmBeforeApply { get; set; } = true;
    public bool ConfirmBeforeDiscardStaged { get; set; } = true;
    public bool BackgroundMetadataRefreshEnabled { get; set; } = true;
}

public sealed class ManagerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object sync = new();
    private readonly string path;

    private ManagerSettingsStore(string path, ManagerSettings current)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.path = path;
        Current = current;
    }

    public ManagerSettings Current { get; }

    public static ManagerSettingsStore Load(string path)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return new ManagerSettingsStore(path, new ManagerSettings());

        try
        {
            var current = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(path), JsonOptions)
                ?? new ManagerSettings();
            return new ManagerSettingsStore(path, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MasterDebugLog.Write("SETTINGS", $"Settings load failed; defaults will be used. path={path}", ex);
            return new ManagerSettingsStore(path, new ManagerSettings());
        }
    }

    public void Update(Action<ManagerSettings> update)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(update);
        lock (sync)
        {
            update(Current);
            SaveCurrent();
        }
    }

    private void SaveCurrent()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var directory = Path.GetDirectoryName(path);
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tempPath, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MasterDebugLog.Write("SETTINGS", $"Settings save failed; the current session will keep the selected values. path={path}", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MasterDebugLog.Write("SETTINGS", $"Could not clean a temporary settings file. path={tempPath}", ex);
            }
        }
    }
}
