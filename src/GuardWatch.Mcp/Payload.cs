using System.Text.Json;
using System.Text.Json.Nodes;

namespace GuardWatch.Mcp;

public static class Payload
{
    public static string ShapeLogs(string json, int maxItems)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject node || node["logs"] is not JsonArray logs)
                return RedactAndLimit(json, maxArray: maxItems);

            var shaped = new JsonArray();
            foreach (var item in logs.Take(maxItems))
                shaped.Add(ShapeLog(item));

            var output = new JsonObject
            {
                ["returned"] = shaped.Count,
                ["logs"] = shaped
            };
            Copy(node, output, "total");
            Copy(node, output, "query_time_ms");
            Copy(node, output, "storage_tier");
            Copy(node, output, "next_cursor");
            if (logs.Count > shaped.Count)
                output["logs_total_in_page"] = logs.Count;

            return output.ToJsonString(JsonOptions.Indented);
        }
        catch (JsonException)
        {
            return LimitText(json);
        }
    }

    public static string RedactAndLimit(string json, int maxChars = 60_000, int maxArray = 40, bool keepLast = false)
    {
        try
        {
            var node = JsonNode.Parse(json);
            Redact(node);
            TrimArrays(node, maxArray, keepLast);
            TruncateStrings(node, 500);
            var text = node?.ToJsonString(JsonOptions.Indented) ?? "null";
            return LimitText(text, maxChars);
        }
        catch (JsonException)
        {
            return LimitText(json, maxChars);
        }
    }

    public static string CompactList(string json, int maxItems = 40)
    {
        try
        {
            var node = JsonNode.Parse(json);
            Redact(node);
            switch (node)
            {
                case JsonArray array:
                    return Finish(WrapArray(array, maxItems));
                case JsonObject obj:
                    foreach (var key in obj.Select(property => property.Key).ToList())
                    {
                        if (obj[key] is JsonArray array && array.Any(item => item is JsonObject))
                        {
                            var total = array.Count;
                            obj[key] = SlimArray(array, maxItems);
                            if (total > maxItems)
                                obj[$"{key}_total"] = total;
                        }
                    }

                    TruncateStrings(obj, 240);
                    return Finish(obj);
                default:
                    return Finish(node);
            }
        }
        catch (JsonException)
        {
            return LimitText(json);
        }
    }

    public static string FilterEvents(string json, bool warningsOnly)
    {
        if (!warningsOnly)
            return CompactList(json, 40);

        try
        {
            var node = JsonNode.Parse(json);
            Redact(node);
            var events = node as JsonArray
                ?? (node as JsonObject)?["events"] as JsonArray
                ?? (node as JsonObject)?["items"] as JsonArray;

            if (events is null)
                return CompactList(json, 40);

            var kept = new JsonArray();
            var recognized = 0;
            foreach (var item in events)
            {
                if (item is not JsonObject obj)
                    continue;

                var kind = EventKind(obj);
                if (kind is not null)
                    recognized++;
                if (kind is "normal" or "info")
                    continue;
                if (kept.Count >= 40)
                    continue;

                kept.Add(Slim(obj));
            }

            var output = new JsonObject
            {
                ["returned"] = kept.Count,
                ["events_total"] = events.Count,
                ["events"] = kept
            };
            if (recognized == 0)
                output["note"] = "A API não informou o tipo do evento; a lista não foi filtrada por Warning.";

            return Finish(output);
        }
        catch (JsonException)
        {
            return LimitText(json);
        }
    }

    public static string RedactText(string text, int maxChars = 50_000)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return RedactAndLimit(text, maxChars);

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var separator = lines[i].IndexOf(':');
            if (separator <= 0)
                continue;

            var key = lines[i][..separator].Trim().Trim('"', '\'');
            if (IsSecret(key))
                lines[i] = lines[i][..(separator + 1)] + " [redigido]";
        }

        return LimitText(string.Join('\n', lines), maxChars);
    }

    public static string LimitText(string text, int maxChars = 50_000)
    {
        if (text.Length <= maxChars)
            return text;

        return text[..maxChars] + "\n...[truncado]";
    }

    public static bool IsSecret(string name)
    {
        var normalized = name.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (normalized is "must_change_password" or "password_changed" or "has_password")
            return false;

        return normalized is "password" or "passwd" or "secret" or "token" or "access_token" or "refresh_token"
                or "id_token" or "kubeconfig" or "authorization" or "client_secret" or "api_key" or "private_key" or "bearer"
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("kubeconfig", StringComparison.Ordinal)
            || normalized.Contains("client_secret", StringComparison.Ordinal)
            || normalized.Contains("private_key", StringComparison.Ordinal)
            || normalized.EndsWith("_token", StringComparison.Ordinal)
            || normalized.EndsWith("_secret", StringComparison.Ordinal);
    }

    private static JsonObject ShapeLog(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return new JsonObject { ["value"] = node?.ToJsonString() };

        var result = new JsonObject();
        foreach (var (key, value) in obj)
        {
            if (value is null || IsSecret(key))
                continue;

            switch (value)
            {
                case JsonValue when value.GetValueKind() == JsonValueKind.String:
                    result[key] = Truncate(value.GetValue<string>(), 500);
                    break;
                case JsonValue:
                    result[key] = value.DeepClone();
                    break;
                case JsonObject nested:
                    var slim = Slim(nested);
                    if (slim.Count > 0 && slim.ToJsonString().Length <= 400)
                        result[key] = slim;
                    break;
            }
        }

        return result;
    }

    private static JsonObject WrapArray(JsonArray array, int maxItems)
    {
        return new JsonObject
        {
            ["returned"] = Math.Min(array.Count, maxItems),
            ["total"] = array.Count,
            ["items"] = SlimArray(array, maxItems)
        };
    }

    private static JsonArray SlimArray(JsonArray array, int maxItems)
    {
        var next = new JsonArray();
        foreach (var item in array.Take(maxItems))
            next.Add(item is JsonObject obj ? Slim(obj) : item?.DeepClone());
        return next;
    }

    private static JsonObject Slim(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (value is null || IsSecret(key))
                continue;

            if (value is JsonValue jsonValue)
            {
                if (jsonValue.GetValueKind() == JsonValueKind.String)
                    result[key] = Truncate(jsonValue.GetValue<string>(), 180);
                else
                    result[key] = jsonValue.DeepClone();
            }
            else if (value is JsonObject nested)
            {
                var slim = Slim(nested);
                if (slim.Count > 0 && slim.ToJsonString().Length <= 300)
                    result[key] = slim;
            }
        }

        return result;
    }

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToList())
                {
                    if (IsSecret(key))
                        obj[key] = "[redigido]";
                    else
                        Redact(obj[key]);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    Redact(item);
                break;
        }
    }

    private static void TrimArrays(JsonNode? node, int max, bool keepLast)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToList())
                {
                    if (obj[key] is JsonArray array && array.Count > max)
                    {
                        var total = array.Count;
                        var selected = keepLast ? array.Skip(total - max) : array.Take(max);
                        var next = new JsonArray();
                        foreach (var item in selected)
                            next.Add(item?.DeepClone());
                        obj[key] = next;
                        obj[$"{key}_total"] = total;
                    }
                    else
                    {
                        TrimArrays(obj[key], max, keepLast);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    TrimArrays(item, max, keepLast);
                break;
        }
    }

    private static void TruncateStrings(JsonNode? node, int max)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                    {
                        var text = value.GetValue<string>();
                        if (text.Length > max)
                            obj[key] = Truncate(text, max);
                    }
                    else
                    {
                        TruncateStrings(obj[key], max);
                    }
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                    {
                        var text = value.GetValue<string>();
                        if (text.Length > max)
                            array[i] = Truncate(text, max);
                    }
                    else
                    {
                        TruncateStrings(array[i], max);
                    }
                }
                break;
        }
    }

    private static string? EventKind(JsonObject obj)
    {
        foreach (var key in new[] { "type", "event_type", "severity", "level" })
        {
            if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                var text = value.GetValue<string>().ToLowerInvariant();
                if (text is "normal" or "info" or "information" or "warning" or "warn" or "error" or "critical")
                    return text is "information" ? "info" : text is "warn" ? "warning" : text;
            }
        }

        return null;
    }

    private static void Copy(JsonObject source, JsonObject target, string name)
    {
        if (source[name] is JsonNode node)
            target[name] = node.DeepClone();
    }

    private static string Finish(JsonNode? node) =>
        LimitText(node?.ToJsonString(JsonOptions.Indented) ?? "null");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
