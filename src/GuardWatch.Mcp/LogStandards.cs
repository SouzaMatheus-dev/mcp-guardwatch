using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GuardWatch.Mcp;

public static partial class LogStandards
{
    public const string Reading =
        "A nota da tela usa structured, severity, correlation, exceptions, noise e sensitive. traces e datadog ficam em summary.criteria_ok.";

    private static readonly string[] Origins = ["apps", "k8s", "container", "agent", "all"];
    private static readonly string[] Statuses = ["padronizado", "parcial", "nao_padronizado", "critico"];
    private static readonly string[] Passed = ["ok", "pass", "passed", "padronizado", "success", "true"];
    private static readonly string[] SampleKeys = ["sample", "samples", "message", "line", "evidence", "raw", "log", "text", "preview", "snippet"];

    public static bool TryWindow(int windowHours, out string? error)
    {
        error = windowHours is 1 or 6 or 24 ? null : "window_hours aceita 1, 6 ou 24.";
        return error is null;
    }

    public static bool TryOrigin(string? origin, out string value, out string? error)
    {
        value = string.IsNullOrWhiteSpace(origin) ? "all" : origin.Trim().ToLowerInvariant();
        error = Origins.Contains(value, StringComparer.Ordinal) ? null : "origin aceita apps, k8s, container, agent ou all.";
        return error is null;
    }

    public static bool TryStatus(string? status, out string? value, out string? error)
    {
        value = string.IsNullOrWhiteSpace(status) ? null : Norm(status);
        error = value is null || Statuses.Contains(value, StringComparer.Ordinal)
            ? null
            : "status aceita padronizado, parcial, nao_padronizado ou critico.";
        return error is null;
    }

    public static string AuditPath(int windowHours, string origin) =>
        ApiQuery.Build("api/v1/admin/log-standards/audit",
            ("window_hours", windowHours.ToString()),
            ("origin", origin),
            ("refresh", "false"));

    public static string DatadogPath(int windowHours, string origin) =>
        ApiQuery.Build("api/v1/admin/log-standards/datadog",
            ("window_hours", windowHours.ToString()),
            ("origin", origin),
            ("refresh", "false"));

    public static string SamplesPath(int windowHours, string origin, string service, string rule, string? env) =>
        ApiQuery.Build("api/v1/admin/log-standards/samples",
            ("window_hours", windowHours.ToString()),
            ("origin", origin),
            ("refresh", "false"),
            ("service", service),
            ("rule", rule),
            ("env", env));

    public static string ShapeAudit(string json, string? service, string? env, string? status, string? rule, int windowHours, string origin)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return MaskedLimit(json);

            var output = new JsonObject
            {
                ["reading"] = Reading,
                ["window_hours"] = windowHours,
                ["origin"] = origin,
                ["payload_keys"] = Keys(root)
            };
            if (root["summary"] is JsonNode summary)
                output["summary"] = ProjectSummary(summary);

            var services = FindServices(root);
            if (services is null)
            {
                output["note"] = "A resposta não trouxe a lista de serviços. O summary foi preservado. As chaves da raiz estão em payload_keys.";
                output["body"] = MaskedLimit(json);
                return Finish(output);
            }

            var matched = services.OfType<JsonObject>()
                .Where(item => Matches(item, service, env, status, rule))
                .ToList();
            var focused = !string.IsNullOrWhiteSpace(service);
            var shown = matched.Take(focused ? 20 : 20).Select(item => ProjectService(item, focused)).ToList();

            output["services_total"] = services.Count;
            output["matched"] = matched.Count;
            output["returned"] = shown.Count;
            output["by_status"] = CountStatus(matched);
            if (!string.IsNullOrWhiteSpace(service))
                output["service"] = service.Trim();
            if (!string.IsNullOrWhiteSpace(env))
                output["env"] = env.Trim();
            if (!string.IsNullOrWhiteSpace(status))
                output["status"] = Norm(status);
            if (!string.IsNullOrWhiteSpace(rule))
                output["rule"] = rule.Trim();
            if (matched.Count == 0)
                output["note"] = "Nenhum serviço passou nos filtros. O summary continua válido para a janela.";
            else if (!focused)
                output["note"] = "Sem service, a lista traz status e o resultado de cada regra, sem a linha de log. Repita com service e env para ver o checks completo.";

            var rows = new JsonArray();
            foreach (var row in shown)
                rows.Add(row);
            output["services"] = rows;
            return Finish(output);
        }
        catch (JsonException)
        {
            return MaskedLimit(json);
        }
    }

    public static string ShapeSamples(string json, string service, string rule)
    {
        var sensitive = rule.Equals("sensitive", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (JsonNode.Parse(json) is not JsonNode root)
                return MaskedLimit(json);

            var lines = new JsonArray();
            var source = FindLines(root, service, rule);
            foreach (var line in source.Take(20))
                lines.Add(Mask(line, sensitive));

            var output = new JsonObject
            {
                ["service"] = service,
                ["rule"] = rule,
                ["masked"] = sensitive,
                ["total"] = source.Count,
                ["returned"] = lines.Count,
                ["lines"] = lines
            };
            if (root is JsonObject obj)
                output["payload_keys"] = Keys(obj);
            if (lines.Count == 0)
            {
                output["note"] = "A resposta não trouxe linhas reconhecíveis. O corpo reduzido está em body.";
                output["body"] = MaskedLimit(json, sensitive);
            }

            return Finish(output);
        }
        catch (JsonException)
        {
            return MaskedLimit(json, sensitive);
        }
    }

    public static string Mask(string text, bool documents)
    {
        text = CpfPunct().Replace(text, "***.***.***-**");
        text = CnpjPunct().Replace(text, "**.***.***/****-**");
        text = Bearer().Replace(text, "$1 [redigido]");
        text = SecretAssign().Replace(text, "$1=[redigido]");
        text = Account().Replace(text, "$1=[redigido]");
        if (!documents)
            return text;

        text = ElevenDigits().Replace(text, "***.***.***-**");
        text = FourteenDigits().Replace(text, "**.***.***/****-**");
        return text;
    }

    private static JsonObject ProjectSummary(JsonNode summary)
    {
        var copy = summary.DeepClone();
        MaskNode(copy, documents: false);
        Truncate(copy, 500);
        return copy as JsonObject ?? new JsonObject { ["value"] = copy };
    }

    private static JsonObject ProjectService(JsonObject source, bool full)
    {
        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (value is null || Payload.IsSecret(key) || key is "checks" or "rules" or "criteria")
                continue;
            if (!full && SampleKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;
            if (value is not JsonValue scalar)
                continue;

            if (scalar.GetValueKind() == JsonValueKind.String)
            {
                var text = Mask(scalar.GetValue<string>(), documents: false);
                if (!full && text.Length > 80 && key is not ("service" or "service_name" or "name" or "env" or "environment" or "status"))
                    continue;
                result[key] = Truncate(text, full ? 300 : 120);
            }
            else
            {
                result[key] = scalar.DeepClone();
            }
        }

        if (ChecksOf(source) is JsonArray checks)
            result["checks"] = ProjectChecks(checks, full);
        return result;
    }

    private static JsonArray ProjectChecks(JsonArray checks, bool full)
    {
        var result = new JsonArray();
        foreach (var item in checks.OfType<JsonObject>())
        {
            var id = CheckId(item);
            var sensitive = id?.Equals("sensitive", StringComparison.OrdinalIgnoreCase) == true;
            var projected = new JsonObject();
            foreach (var (key, value) in item)
            {
                if (value is null || Payload.IsSecret(key))
                    continue;
                if (!full && SampleKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (value is JsonValue scalar)
                {
                    if (scalar.GetValueKind() == JsonValueKind.String)
                        projected[key] = Truncate(Mask(scalar.GetValue<string>(), sensitive), full ? 300 : 80);
                    else
                        projected[key] = scalar.DeepClone();
                }
                else if (full && value is JsonArray array)
                {
                    var kept = new JsonArray();
                    foreach (var entry in array.Take(3))
                    {
                        if (entry is JsonValue text && text.GetValueKind() == JsonValueKind.String)
                            kept.Add(Truncate(Mask(text.GetValue<string>(), sensitive), 300));
                        else if (entry is JsonValue other)
                            kept.Add(other.DeepClone());
                    }

                    projected[key] = kept;
                    if (array.Count > kept.Count)
                        projected[$"{key}_total"] = array.Count;
                }
            }

            if (projected.Count > 0)
                result.Add(projected);
        }

        return result;
    }

    private static bool Matches(JsonObject item, string? service, string? env, string? status, string? rule)
    {
        if (!string.IsNullOrWhiteSpace(service) && !Equals(Read(item, "service", "service_name", "name"), service))
            return false;
        if (!string.IsNullOrWhiteSpace(env) && !Equals(Read(item, "env", "environment"), env))
            return false;
        if (!string.IsNullOrWhiteSpace(status) && Norm(Read(item, "status", "grade", "classification", "result") ?? "") != Norm(status))
            return false;
        if (!string.IsNullOrWhiteSpace(rule) && !MatchesRule(item, rule))
            return false;
        return true;
    }

    private static bool MatchesRule(JsonObject item, string rule)
    {
        if (ChecksOf(item) is not JsonArray checks)
            return false;

        foreach (var check in checks.OfType<JsonObject>())
        {
            if (!Equals(CheckId(check), rule))
                continue;
            return PassedState(check) != true;
        }

        return false;
    }

    private static bool? PassedState(JsonObject check)
    {
        foreach (var key in new[] { "ok", "passed", "pass" })
        {
            if (check[key] is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                return flag.GetValue<bool>();
        }

        var status = Read(check, "status", "result", "grade", "level");
        if (status is null)
            return null;

        var normalized = Norm(status);
        if (Passed.Contains(normalized, StringComparer.Ordinal))
            return true;
        return false;
    }

    private static JsonObject CountStatus(IReadOnlyList<JsonObject> services)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var status in Statuses)
            counts[status] = 0;
        foreach (var service in services)
        {
            var status = Norm(Read(service, "status", "grade", "classification", "result") ?? "");
            if (status.Length == 0)
                status = "sem_status";
            counts[status] = counts.TryGetValue(status, out var count) ? count + 1 : 1;
        }

        var output = new JsonObject();
        foreach (var (status, count) in counts)
            output[status] = count;
        return output;
    }

    private static JsonArray? FindServices(JsonObject root)
    {
        foreach (var name in new[] { "services", "items", "results", "rows" })
        {
            if (root[name] is JsonArray array)
                return array;
        }

        if (root["data"] is JsonArray data)
            return data;
        if (root["data"] is JsonObject nested)
        {
            foreach (var name in new[] { "services", "items", "results", "rows" })
            {
                if (nested[name] is JsonArray array)
                    return array;
            }
        }

        foreach (var property in root)
        {
            if (property.Value is JsonArray array && array.Any(item => item is JsonObject obj && (obj["checks"] is not null || obj["status"] is not null || obj["service"] is not null)))
                return array;
        }

        return null;
    }

    private static JsonArray? ChecksOf(JsonObject service)
    {
        foreach (var name in new[] { "checks", "rules", "criteria" })
        {
            if (service[name] is JsonArray array)
                return array;
        }

        return null;
    }

    private static List<string> FindLines(JsonNode root, string service, string rule)
    {
        JsonArray? array = root switch
        {
            JsonArray direct => direct,
            JsonObject obj => NamedLines(obj),
            _ => null
        };
        if (array is null && root is JsonObject container)
        {
            foreach (var property in container)
            {
                if (property.Value is JsonArray candidate && candidate.Count > 0)
                {
                    array = candidate;
                    break;
                }
            }
        }

        var lines = new List<string>();
        if (array is null)
            return lines;

        foreach (var item in array)
        {
            switch (item)
            {
                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    lines.Add(value.GetValue<string>());
                    break;
                case JsonObject obj:
                    var lineService = Read(obj, "service", "service_name");
                    var lineRule = Read(obj, "rule", "rule_id");
                    if (lineService is not null && !Equals(lineService, service))
                        continue;
                    if (lineRule is not null && !Equals(lineRule, rule))
                        continue;
                    var text = Read(obj, "message", "line", "text", "log", "sample", "raw", "content", "preview");
                    if (!string.IsNullOrWhiteSpace(text))
                        lines.Add(text);
                    break;
            }
        }

        return lines;
    }

    private static JsonArray? NamedLines(JsonObject obj)
    {
        foreach (var name in new[] { "samples", "lines", "items", "results", "logs", "events" })
        {
            if (obj[name] is JsonArray array)
                return array;
        }

        if (obj["data"] is JsonArray data)
            return data;
        if (obj["data"] is JsonObject nested)
            return NamedLines(nested);
        return null;
    }

    private static string? CheckId(JsonObject check) =>
        Read(check, "id", "rule", "rule_id", "name", "key");

    private static string? Read(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                var text = value.GetValue<string>().Trim();
                if (text.Length > 0)
                    return text;
            }
        }

        return null;
    }

    private static bool Equals(string? left, string? right) =>
        left is not null && right is not null && Norm(left) == Norm(right);

    private static string Norm(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace("ã", "a", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("â", "a", StringComparison.Ordinal)
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("ê", "e", StringComparison.Ordinal)
            .Replace("í", "i", StringComparison.Ordinal)
            .Replace("ó", "o", StringComparison.Ordinal)
            .Replace("õ", "o", StringComparison.Ordinal)
            .Replace("ú", "u", StringComparison.Ordinal)
            .Replace("ç", "c", StringComparison.Ordinal)
            .Replace(' ', '_')
            .Replace('-', '_');
        return normalized;
    }

    private static JsonArray Keys(JsonObject obj)
    {
        var keys = new JsonArray();
        foreach (var property in obj)
            keys.Add(property.Key);
        return keys;
    }

    private static void MaskNode(JsonNode? node, bool documents)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToList())
                {
                    if (Payload.IsSecret(key))
                        obj[key] = "[redigido]";
                    else if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                        obj[key] = Mask(value.GetValue<string>(), documents);
                    else
                        MaskNode(obj[key], documents);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                        array[i] = Mask(value.GetValue<string>(), documents);
                    else
                        MaskNode(array[i], documents);
                }

                break;
        }
    }

    private static void Truncate(JsonNode? node, int max)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(property => property.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                        obj[key] = Truncate(value.GetValue<string>(), max);
                    else
                        Truncate(obj[key], max);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                        array[i] = Truncate(value.GetValue<string>(), max);
                    else
                        Truncate(array[i], max);
                }

                break;
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private static string Finish(JsonNode node) =>
        Payload.LimitText(node.ToJsonString(JsonOptions.Indented));

    private static string MaskedLimit(string json, bool documents = false)
    {
        try
        {
            var node = JsonNode.Parse(json);
            MaskNode(node, documents);
            return Payload.RedactAndLimit(node?.ToJsonString() ?? json);
        }
        catch (JsonException)
        {
            return Payload.LimitText(Mask(json, documents));
        }
    }

    [GeneratedRegex(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b")]
    private static partial Regex CpfPunct();

    [GeneratedRegex(@"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b")]
    private static partial Regex CnpjPunct();

    [GeneratedRegex(@"\b\d{11}\b")]
    private static partial Regex ElevenDigits();

    [GeneratedRegex(@"\b\d{14}\b")]
    private static partial Regex FourteenDigits();

    [GeneratedRegex(@"(?i)\b(bearer)\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i)\b(password|passwd|senha|token|secret|api_key|apikey|authorization|credential|client_secret)\b\s*[:=]\s*\S+")]
    private static partial Regex SecretAssign();

    [GeneratedRegex(@"(?i)\b(conta|account|agencia|agência)\b\s*[:=]?\s*\d{4,}")]
    private static partial Regex Account();
}
