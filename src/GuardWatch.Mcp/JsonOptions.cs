using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardWatch.Mcp;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
