namespace MhwModManager.Core;

public enum TextureSafetySeverity { Safe, Warning, Error }

public sealed record TextureSafetyResult(
    TextureSafetySeverity Severity,
    string Code,
    string Message);

/// <summary>
/// Cheap fail-closed checks for obvious broken MHW TEX sources. This is intentionally not a full
/// TEX decoder; valid-looking files can still be incompatible with a particular model/material or
/// GPU/driver combination. The goal is to stop truncated/non-TEX data before deployment.
/// </summary>
public static class TextureSafetyInspector
{
    public static TextureSafetyResult InspectTexHeader(ReadOnlySpan<byte> prefix, long length)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"length={length}; prefixBytes={prefix.Length}");
        if (length < 16)
            return new(TextureSafetySeverity.Error, "TEX_TRUNCATED", $"Texture is only {length} byte(s), which is too small to contain a valid MHW TEX header.");
        if (prefix.Length < 3)
            return new(TextureSafetySeverity.Error, "TEX_HEADER_UNREADABLE", "Texture header could not be read.");
        if (prefix[0] != (byte)'T' || prefix[1] != (byte)'E' || prefix[2] != (byte)'X')
            return new(TextureSafetySeverity.Error, "TEX_BAD_MAGIC", "File has a .tex extension but does not begin with the expected TEX signature.");
        return new(TextureSafetySeverity.Safe, "TEX_HEADER_OK", "TEX signature and minimum header size look valid.");
    }
}
