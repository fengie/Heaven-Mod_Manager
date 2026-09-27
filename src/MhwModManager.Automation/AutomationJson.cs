using MhwModManager.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MhwModManager.Automation;

internal static class AutomationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
