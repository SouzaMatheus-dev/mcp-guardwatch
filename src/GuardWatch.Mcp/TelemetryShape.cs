using System.Text.Json;
using System.Text.Json.Nodes;

namespace GuardWatch.Mcp;

public static class TelemetryShape
{
    public static string Erp(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return Payload.RedactAndLimit(json);

            if (root["summary"] is null && root["top_screens"] is null && root["traces"] is null && root["logs"] is null)
                return Payload.RedactAndLimit(json);

            var output = new JsonObject();
            if (root["summary"] is JsonNode summary)
                output["summary"] = summary.DeepClone();
            CopyArray(root, output, "top_screens", 20);
            CopyArray(root, output, "traces", 15);
            CopyArray(root, output, "logs", 15);
            return LogStandards.Mask(Payload.RedactAndLimit(output.ToJsonString(), maxArray: 20), documents: false);
        }
        catch (JsonException)
        {
            return Payload.LimitText(json);
        }
    }

    private static void CopyArray(JsonObject source, JsonObject target, string name, int max)
    {
        if (source[name] is not JsonArray array)
            return;

        var kept = new JsonArray();
        foreach (var item in array.Take(max))
            kept.Add(item?.DeepClone());
        target[name] = kept;
        target[$"{name}_total"] = array.Count;
    }
}
