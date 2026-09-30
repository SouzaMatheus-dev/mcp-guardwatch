using System.Text.Json;

namespace GuardWatch.Mcp;

public sealed record GuardWatchEnvironment(string Name, string Env, string Suffix)
{
    public string Tier => EnvironmentCatalog.NormalizeTier(Env)
        ?? EnvironmentCatalog.NormalizeTier(Suffix)
        ?? EnvironmentCatalog.NormalizeTier(Name)
        ?? "";
}

public static class EnvironmentCatalog
{
    public static IReadOnlyList<GuardWatchEnvironment> Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var found = new List<GuardWatchEnvironment>();
            Collect(document.RootElement, found, 0);
            return found
                .GroupBy(item => $"{item.Name}\u001f{item.Env}\u001f{item.Suffix}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Tier, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<GuardWatchEnvironment> Matching(IEnumerable<GuardWatchEnvironment> environments, string tier)
    {
        var wanted = NormalizeTier(tier);
        if (wanted is null)
            return [];

        return environments.Where(item => item.Tier == wanted).ToList();
    }

    public static string? NormalizeTier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var token = value.Trim().Trim('-', '_', '/', ' ').ToLowerInvariant();
        var last = token.Split(['-', '_', '/', ' '], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? token;
        return last switch
        {
            "prd" or "prod" or "production" or "producao" => "prd",
            "hml" or "hom" or "homol" or "homolog" or "homologacao" or "staging" or "stg" => "hml",
            "dev" or "des" or "desenv" or "development" => "dev",
            _ => null
        };
    }

    private static void Collect(JsonElement element, List<GuardWatchEnvironment> found, int depth)
    {
        if (depth > 6)
            return;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array && IsEnvironmentKey(property.Name))
                    {
                        foreach (var item in property.Value.EnumerateArray())
                            AddItem(item, found);
                        continue;
                    }

                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Collect(property.Value, found, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, found, depth + 1);
                break;
        }
    }

    private static void AddItem(JsonElement item, List<GuardWatchEnvironment> found)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                found.Add(FromLabel(text));
            return;
        }

        if (item.ValueKind == JsonValueKind.Object && TryObject(item, out var environment))
            found.Add(environment);
    }

    private static bool TryObject(JsonElement element, out GuardWatchEnvironment environment)
    {
        environment = null!;
        var env = First(element, "env", "environment", "ambiente");
        var suffix = First(element, "suffix", "sufixo", "env_suffix");
        var name = First(element, "name", "label", "value", "id");
        if (env is null && suffix is null)
            return false;
        if (NormalizeTier(env) is null && NormalizeTier(suffix) is null && NormalizeTier(name) is null)
            return false;

        name ??= env ?? suffix ?? "";
        environment = new GuardWatchEnvironment(name, env ?? "", suffix ?? InferSuffix(name));
        return true;
    }

    private static GuardWatchEnvironment FromLabel(string label)
    {
        var tier = NormalizeTier(label) ?? "";
        return new GuardWatchEnvironment(label.Trim(), tier, InferSuffix(label));
    }

    private static string InferSuffix(string label)
    {
        var tier = NormalizeTier(label);
        if (tier is null)
            return "";

        var trimmed = label.Trim();
        var index = trimmed.LastIndexOf(tier, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
            return tier;

        var marker = trimmed[index - 1];
        return marker is '-' or '_' or '/' ? trimmed[(index - 1)..] : tier;
    }

    private static string? First(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (property.Value.ValueKind == JsonValueKind.String)
                    return string.IsNullOrWhiteSpace(property.Value.GetString()) ? null : property.Value.GetString()!.Trim();
                if (property.Value.ValueKind == JsonValueKind.Number)
                    return property.Value.ToString();
            }
        }

        return null;
    }

    private static bool IsEnvironmentKey(string name) =>
        name.Contains("env", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ambient", StringComparison.OrdinalIgnoreCase);
}
