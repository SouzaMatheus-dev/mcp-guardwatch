using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GuardWatch.Mcp;

public sealed class GuardWatchClient
{
    private readonly HttpClient _http;
    private readonly GuardWatchSettings _settings;
    private readonly TokenStore _store;
    private readonly ILogger<GuardWatchClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    public GuardWatchClient(HttpClient http, GuardWatchSettings settings, TokenStore store, ILogger<GuardWatchClient> logger)
    {
        _http = http;
        _settings = settings;
        _store = store;
        _logger = logger;
    }

    public Task<string> GetMeAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/auth/me", cancellationToken);

    public Task<string> QueryLogsAsync(LogSearch search, CancellationToken cancellationToken) =>
        PostAsync("api/v1/v2/logs/query", search.ToQueryBody(), cancellationToken);

    public Task<string> LogHistogramAsync(LogSearch search, int buckets, CancellationToken cancellationToken) =>
        PostAsync("api/v1/logs/histogram", search.ToHistogramBody(buckets), cancellationToken);

    public Task<string> LogPatternsAsync(LogSearch search, CancellationToken cancellationToken) =>
        PostAsync("api/v1/logs/patterns", search.ToPatternBody(), cancellationToken);

    public Task<string> LogFacetsAsync(LogSearch search, CancellationToken cancellationToken) =>
        PostAsync("api/v1/logs/facets", search.ToFacetBody(), cancellationToken);

    public Task<string> GetLogStatsAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/logs/stats", cancellationToken);

    public Task<string> GetLogServicesAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/logs/services", cancellationToken);

    public Task<string> GetLogsByTraceAsync(string traceId, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/logs/trace/{Uri.EscapeDataString(traceId)}", cancellationToken);

    public Task<string> GetMachinesAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/machines", cancellationToken);

    public Task<string> GetDashboardSummaryAsync(string? machineId, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(machineId)
            ? "api/v1/dashboard/summary"
            : $"api/v1/dashboard/summary?machine_id={Uri.EscapeDataString(machineId)}";
        return GetAsync(path, cancellationToken);
    }

    public Task<string> GetMachineMetricsAsync(string machineId, int limit, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/metrics/{Uri.EscapeDataString(machineId)}?limit={Math.Clamp(limit, 1, 50)}", cancellationToken);

    public Task<string> GetMetricsHistoryAsync(string machineId, int hours, string interval, CancellationToken cancellationToken)
    {
        var (start, end) = TimeWindow.LastHours(hours);
        var safeInterval = interval is "minute" or "raw" or "hourly" ? interval : "hourly";
        var path = $"api/v1/metrics/history/{Uri.EscapeDataString(machineId)}"
            + $"?start_time={Uri.EscapeDataString(start)}&end_time={Uri.EscapeDataString(end)}&interval={safeInterval}";
        return GetAsync(path, cancellationToken);
    }

    public Task<string> GetAlertSummaryAsync(int hours, CancellationToken cancellationToken) =>
        GetAsync($"api/alerts/stats/summary?hours={Math.Clamp(hours, 1, 168)}", cancellationToken);

    public Task<string> GetK8sClustersAsync(CancellationToken cancellationToken) =>
        GetAsync("api/v1/k8s/clusters", cancellationToken);

    public Task<string> GetK8sSnapshotAsync(string clusterId, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/snapshot", cancellationToken);

    public Task<string> GetK8sPodsAsync(string clusterId, string? ns, CancellationToken cancellationToken)
    {
        var path = $"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/pods";
        if (!string.IsNullOrWhiteSpace(ns))
            path += $"?namespace={Uri.EscapeDataString(ns)}";
        return GetAsync(path, cancellationToken);
    }

    public Task<string> GetK8sDeploymentsAsync(string clusterId, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/deployments", cancellationToken);

    public Task<string> GetK8sNamespacesAsync(string clusterId, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/namespaces", cancellationToken);

    public Task<string> GetK8sEventsAsync(string clusterId, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/events", cancellationToken);

    public Task<string> GetK8sPodLogsAsync(string clusterId, string ns, string pod, string? container, int tail, bool previous, CancellationToken cancellationToken)
    {
        var query = new List<string> { $"tail={Math.Clamp(tail, 1, 1000)}" };
        if (!string.IsNullOrWhiteSpace(container))
            query.Add($"container={Uri.EscapeDataString(container)}");
        if (previous)
            query.Add("previous=true");

        var path = $"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/pods/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(pod)}/logs?{string.Join('&', query)}";
        return GetTextAsync(path, cancellationToken);
    }

    public Task<string> GetK8sPodUsageAsync(string clusterId, string ns, string pod, int minutes, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/pod-usage?namespace={Uri.EscapeDataString(ns)}&pod={Uri.EscapeDataString(pod)}&minutes={Math.Clamp(minutes, 1, 1440)}", cancellationToken);

    public Task<string> GetK8sNodeUsageAsync(string clusterId, string node, int minutes, CancellationToken cancellationToken) =>
        GetAsync($"api/v1/k8s/clusters/{Uri.EscapeDataString(clusterId)}/node-usage?node={Uri.EscapeDataString(node)}&minutes={Math.Clamp(minutes, 1, 1440)}", cancellationToken);

    public Task<string> ReadJsonAsync(string path, CancellationToken cancellationToken) =>
        GetAsync(path, cancellationToken);

    public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        GetTextAsync(path, cancellationToken);

    private Task<string> GetAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, true, cancellationToken);

    private Task<string> GetTextAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, null, true, cancellationToken);

    private Task<string> PostAsync(string path, IReadOnlyDictionary<string, object?> body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body, JsonOptions.Web), true, cancellationToken);

    private async Task<string> SendAsync(HttpMethod method, string path, string? json, bool allowRetry, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(method, path, json, token, allowRetry, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
            }
        }
    }

    private async Task<string> SendOnceAsync(HttpMethod method, string path, string? json, string token, bool allowRetry, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && allowRetry)
        {
            Invalidate();
            return await SendAsync(method, path, json, false, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new GuardWatchException(FormatError((int)response.StatusCode, body));

        return body;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null)
            return _token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null)
                return _token;

            var cached = _store.TryRead();
            if (cached is not null && await ProbeAsync(cached, cancellationToken))
            {
                _logger.LogInformation("Sessão GuardWatch reutilizada.");
                return _token = cached;
            }

            if (!string.IsNullOrWhiteSpace(_settings.Token) && await ProbeAsync(_settings.Token, cancellationToken))
            {
                _store.Save(_settings.Token);
                return _token = _settings.Token;
            }

            if (string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
            {
                throw new GuardWatchException(
                    "Credenciais ausentes. Copie guardwatch.local.example.json para guardwatch.local.json e preencha o usuário LDAP, ou defina GUARDWATCH_TOKEN.");
            }

            if (_settings.LdapProviderId <= 0)
            {
                throw new GuardWatchException(
                    "Defina ldapProviderId ou GUARDWATCH_LDAP_PROVIDER_ID. O id aparece em GET /api/v1/auth/providers.");
            }

            var login = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["provider_id"] = _settings.LdapProviderId,
                ["username"] = _settings.Username,
                ["password"] = _settings.Password
            }, JsonOptions.Web);

            using var response = await SendWithRetry(
                () => new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/ldap/login")
                {
                    Content = new StringContent(login, Encoding.UTF8, "application/json")
                },
                cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new GuardWatchException("Falha no login LDAP: " + FormatError((int)response.StatusCode, text));

            var token = ReadToken(text);
            _store.Save(token);
            _logger.LogInformation("Login LDAP concluído para {User}.", _settings.Username);
            return _token = token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ProbeAsync(string token, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetry(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return false;

        if (!response.IsSuccessStatusCode)
            throw new GuardWatchException(FormatError((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));

        return true;
    }

    private async Task<HttpResponseMessage> SendWithRetry(Func<HttpRequestMessage> create, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _http.SendAsync(create(), cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
            }
        }
    }

    private void Invalidate()
    {
        _token = null;
        _store.Clear();
    }

    private static string ReadToken(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var name in new[] { "access_token", "accessToken" })
        {
            if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var token = value.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }
        }

        throw new GuardWatchException("Login LDAP não devolveu access_token.");
    }

    private static string FormatError(int status, string body)
    {
        var detail = ExtractDetail(body);
        return string.IsNullOrWhiteSpace(detail)
            ? $"GuardWatch respondeu HTTP {status}."
            : $"GuardWatch respondeu HTTP {status}: {detail}";
    }

    private static string ExtractDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("detail", out var detail))
                return Payload.LimitText(body, 400);

            return detail.ValueKind switch
            {
                JsonValueKind.String => detail.GetString() ?? "",
                JsonValueKind.Array => string.Join("; ", detail.EnumerateArray().Select(item =>
                    item.ValueKind == JsonValueKind.Object && item.TryGetProperty("msg", out var msg)
                        ? msg.GetString()
                        : item.ToString())),
                _ => detail.ToString()
            };
        }
        catch (JsonException)
        {
            return Payload.LimitText(body, 400);
        }
    }
}

public sealed record LogSearch(
    string? Query,
    int Minutes,
    string? Start,
    string? End,
    string? MachineId,
    string? ClusterName,
    string? Namespace,
    string? Service,
    string? Severity,
    string? Env,
    string? ErrorType,
    bool ExcludeNoise,
    int Limit,
    string Sort)
{
    public Dictionary<string, object?> ToQueryBody()
    {
        var (start, end) = TimeWindow.Resolve(Minutes, Start, End);
        var body = new Dictionary<string, object?>
        {
            ["start_time"] = start,
            ["end_time"] = end,
            ["exclude_noise"] = ExcludeNoise,
            ["limit"] = Math.Clamp(Limit, 1, 100),
            ["offset"] = 0,
            ["sort"] = Sort == "asc" ? "asc" : "desc"
        };
        Add(body, "query", BuildQueryText());
        Add(body, "machine_id", MachineId);
        Add(body, "cluster_name", ClusterName);
        Add(body, "namespace", Namespace);
        return body;
    }

    private string? BuildQueryText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query))
            parts.Add(Query.Trim());

        AddTerm(parts, "service", Service);
        AddTerm(parts, "severity", Severity);
        AddTerm(parts, "env", Env);
        AddTerm(parts, "error_type", ErrorType);
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static void AddTerm(List<string> parts, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var trimmed = value.Trim().Replace("\"", "\\\"", StringComparison.Ordinal);
        var term = trimmed.Any(ch => char.IsWhiteSpace(ch) || ch is ':' or '/')
            ? $"\"{trimmed}\""
            : trimmed;
        parts.Add($"{field}:{term}");
    }

    public Dictionary<string, object?> ToHistogramBody(int buckets)
    {
        var body = BaseFilter();
        body["buckets"] = Math.Clamp(buckets, 5, 120);
        return body;
    }

    public Dictionary<string, object?> ToPatternBody() => BaseFilter();

    public Dictionary<string, object?> ToFacetBody() => BaseFilter();

    private Dictionary<string, object?> BaseFilter()
    {
        var (start, end) = TimeWindow.Resolve(Minutes, Start, End);
        var body = new Dictionary<string, object?>
        {
            ["start_time"] = start,
            ["end_time"] = end,
            ["exclude_noise"] = ExcludeNoise
        };
        Add(body, "query", BuildQueryText());
        Add(body, "machine_id", MachineId);
        Add(body, "cluster_name", ClusterName);
        Add(body, "namespace", Namespace);
        Add(body, "service", Service);
        Add(body, "severity", Severity);
        Add(body, "env", Env);
        Add(body, "error_type", ErrorType);
        return body;
    }

    private static void Add(Dictionary<string, object?> body, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            body[name] = value.Trim();
    }
}
