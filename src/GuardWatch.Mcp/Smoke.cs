using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardWatch.Mcp;

public static class Smoke
{
    public static async Task<int> RunAsync()
    {
        var settings = GuardWatchSettings.Load();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(90)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("McpGuardWatch/0.3");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var client = new GuardWatchClient(http, settings, TokenStore.CreateDefault(), NullLogger<GuardWatchClient>.Instance);
        try
        {
            Console.WriteLine("== whoami ==");
            Console.WriteLine(Payload.RedactAndLimit(await client.GetMeAsync(CancellationToken.None), maxChars: 2500));

            Console.WriteLine("== log_stats ==");
            Console.WriteLine(Payload.RedactAndLimit(await client.GetLogStatsAsync(CancellationToken.None), maxChars: 4000));

            Console.WriteLine("== clusters ==");
            Console.WriteLine(Payload.CompactList(await client.GetK8sClustersAsync(CancellationToken.None), 15));

            var errors = new LogSearch(null, 60, null, null, null, null, null, null, "error", null, null, true, 5, "desc");
            Console.WriteLine("== query_logs error 60m ==");
            Console.WriteLine(Payload.ShapeLogs(await client.QueryLogsAsync(errors, CancellationToken.None), 5));
            return 0;
        }
        catch (Exception ex) when (ex is GuardWatchException or HttpRequestException or TaskCanceledException)
        {
            return Fail(ex);
        }
    }

    public static async Task<int> InvestigateAsync(string term)
    {
        var client = CreateClient();
        try
        {
            foreach (var hours in new[] { 6, 24 })
            {
                var minutes = hours * 60;
                var all = new LogSearch(term, minutes, null, null, null, null, null, null, null, "prod", null, true, 1, "desc");
                var errors = new LogSearch(term, minutes, null, null, null, null, null, null, "error", "prod", null, true, 1, "desc");
                var warnings = new LogSearch(term, minutes, null, null, null, null, null, null, "warning", "prod", null, true, 1, "desc");
                var allJson = await client.QueryLogsAsync(all, CancellationToken.None);
                var errorJson = await client.QueryLogsAsync(errors, CancellationToken.None);
                var warningJson = await client.QueryLogsAsync(warnings, CancellationToken.None);
                Console.WriteLine($"== volume prod {hours}h ==");
                Console.WriteLine($"todos={ReadTotal(allJson)} erros={ReadTotal(errorJson)} warnings={ReadTotal(warningJson)}");
            }

            var sample = new LogSearch(term, 360, null, null, null, null, null, null, null, "prod", null, true, 30, "desc");
            Console.WriteLine("== amostra 6h ==");
            Console.WriteLine(Payload.ShapeLogs(await client.QueryLogsAsync(sample, CancellationToken.None), 30));

            var errorSample = new LogSearch(term, 1440, null, null, null, null, null, null, "error", "prod", null, true, 12, "desc");
            Console.WriteLine("== erros 24h ==");
            Console.WriteLine(Payload.ShapeLogs(await client.QueryLogsAsync(errorSample, CancellationToken.None), 12));

            var patterns = new LogSearch(term, 360, null, null, null, null, null, null, "error", "prod", null, true, 20, "desc");
            Console.WriteLine("== padroes de erro 6h ==");
            Console.WriteLine(Payload.RedactAndLimit(await client.LogPatternsAsync(patterns, CancellationToken.None), maxArray: 15));

            var clusters = JsonDocument.Parse(await client.GetK8sClustersAsync(CancellationToken.None));
            foreach (var cluster in Enumerate(clusters.RootElement))
            {
                var id = cluster.TryGetProperty("id", out var idValue) ? idValue.ToString() : "";
                var name = cluster.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : id;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                try
                {
                    var deployments = await client.GetK8sDeploymentsAsync(id, CancellationToken.None);
                    if (deployments.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"== deployments {name} ==");
                        Console.WriteLine(FilterJson(deployments, term));
                    }

                    var pods = await client.GetK8sPodsAsync(id, null, CancellationToken.None);
                    if (pods.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"== pods {name} ==");
                        Console.WriteLine(FilterJson(pods, term));
                    }
                }
                catch (GuardWatchException ex)
                {
                    Console.WriteLine($"== cluster {name} indisponivel: {ex.Message}");
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is GuardWatchException or HttpRequestException or TaskCanceledException)
        {
            return Fail(ex);
        }
    }

    private static GuardWatchClient CreateClient()
    {
        var settings = GuardWatchSettings.Load();
        var http = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(90)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("McpGuardWatch/0.3");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return new GuardWatchClient(http, settings, TokenStore.CreateDefault(), NullLogger<GuardWatchClient>.Instance);
    }

    private static int ReadTotal(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("total", out var total) && total.TryGetInt32(out var value) ? value : -1;
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                yield return item;
            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in property.Value.EnumerateArray())
                    yield return item;
                yield break;
            }
        }
    }

    private static string FilterJson(string json, string term)
    {
        using var document = JsonDocument.Parse(json);
        var matches = Enumerate(document.RootElement)
            .Where(item => item.GetRawText().Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(item => item.GetRawText())
            .ToArray();
        return Payload.RedactAndLimit("[" + string.Join(',', matches) + "]", maxArray: 20);
    }

    private static int Fail(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
            Console.Error.WriteLine($"{current.GetType().Name}: {current.Message}");
        return 1;
    }
}
