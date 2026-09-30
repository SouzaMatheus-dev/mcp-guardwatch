namespace GuardWatch.Mcp;

public static class ApiQuery
{
    public static string Build(string path, params (string Name, string? Value)[] pairs)
    {
        var query = string.Join('&', pairs
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Name)}={Uri.EscapeDataString(pair.Value!.Trim())}"));

        return query.Length == 0 ? path : $"{path}?{query}";
    }

    public static string Cluster(string clusterId, string resource) =>
        $"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/{resource}";
}
