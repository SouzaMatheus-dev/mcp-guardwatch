using ModelContextProtocol.Client;

namespace GuardWatch.Mcp.Tests;

public sealed class McpServerTests
{
    [Fact]
    public async Task Servidor_publica_ferramentas_de_log_e_metrica()
    {
        var project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GuardWatch.Mcp", "GuardWatch.Mcp.csproj"));
        Assert.True(File.Exists(project), project);

        var tfmDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var configuration = tfmDirectory.Parent?.Name;
        if (configuration is not ("Debug" or "Release"))
            configuration = "Debug";

        var server = Path.Combine(tfmDirectory.FullName, "..", "..", "..", "..", "..", "src", "GuardWatch.Mcp", "bin", configuration, tfmDirectory.Name, "GuardWatch.Mcp.dll");
        server = Path.GetFullPath(server);
        Assert.True(File.Exists(server), server);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "guardwatch",
            Command = "dotnet",
            Arguments = [server],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GUARDWATCH_BASE_URL"] = "https://guardwatch.example"
            }
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("list_environments", names);
        Assert.Contains("query_logs", names);
        Assert.Contains("log_patterns", names);
        Assert.Contains("machine_metrics", names);
        Assert.Contains("k8s_pod_logs", names);
        Assert.Contains("k8s_pod_usage", names);
        Assert.Contains("list_k8s_nodes", names);
        Assert.Contains("apm_overview", names);
        Assert.Contains("dbm_overview", names);
        Assert.Contains("siem_overview", names);
        Assert.Contains("list_health_checks", names);
        Assert.Contains("rca_cases", names);
        Assert.DoesNotContain("create_k8s_cluster", names);
    }
}
