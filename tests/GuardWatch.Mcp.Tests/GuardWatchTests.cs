using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using GuardWatch.Mcp;
using GuardWatch.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: SupportedOSPlatform("windows")]

namespace GuardWatch.Mcp.Tests;

public sealed class PromptTests
{
    [Fact]
    public void Investigacao_restringe_ao_ambiente_escolhido()
    {
        var text = GuardWatchPrompts.Investigar("pix-chaves", "HML");

        Assert.Contains("HML", text, StringComparison.Ordinal);
        Assert.Contains("env HML", text, StringComparison.Ordinal);
        Assert.Contains("PRD, HML ou DEV", text, StringComparison.Ordinal);
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void Merge_prefere_ambiente_e_descarta_caminho_da_url()
    {
        var file = new GuardWatchSettings
        {
            BaseUrl = "https://guardwatch.example/kubernetes",
            Username = "arquivo",
            LdapProviderId = 5
        };

        var merged = GuardWatchSettings.Merge(file, "https://guardwatch.example/login", "9", "rede", null, "token");

        Assert.Equal("https://guardwatch.example", merged.BaseUrl);
        Assert.Equal(9, merged.LdapProviderId);
        Assert.Equal("rede", merged.Username);
        Assert.Equal("token", merged.Token);
    }

    [Fact]
    public void Consulta_poe_severidade_no_texto_porque_a_api_ignora_o_campo()
    {
        var body = new LogSearch(
            null, 15,
            "2026-01-01T00:00:00.000Z", "2026-01-01T01:00:00.000Z",
            null, null, "kafka", "pix-chaves", "error", null, null, true, 10, "desc").ToQueryBody();

        Assert.Equal("service:pix-chaves severity:error", body["query"]);
        Assert.Equal("service:pix-chaves severity:error", new LogSearch(
            null, 15,
            "2026-01-01T00:00:00.000Z", "2026-01-01T01:00:00.000Z",
            null, null, "kafka", "pix-chaves", "error", null, null, true, 10, "desc").ToPatternBody()["query"]);
        Assert.False(body.ContainsKey("severity"));
        Assert.Equal("kafka", body["namespace"]);
    }

    [Fact]
    public void Normalize_rejeita_url_invalida()
    {
        var error = Assert.Throws<GuardWatchException>(() => GuardWatchSettings.NormalizeBaseUrl("nao-e-url"));
        Assert.Contains("inválida", error.Message);
    }
}

public sealed class TokenStoreTests
{
    [Fact]
    public void Roundtrip_protege_o_token()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-mcp-tests", Guid.NewGuid().ToString("n"), "token.bin");
        var store = new TokenStore(path);
        store.Save("segredo-de-teste");

        var stored = File.ReadAllBytes(path);
        var plain = System.Text.Encoding.UTF8.GetBytes("segredo-de-teste");
        Assert.False(stored.AsSpan().IndexOf(plain) >= 0);
        Assert.Equal("segredo-de-teste", store.TryRead());

        store.Clear();
        Assert.Null(store.TryRead());
    }
}

public sealed class PayloadTests
{
    [Fact]
    public void Redige_kubeconfig_e_resume_logs()
    {
        const string json = """
            {
              "total": 2,
              "query_time_ms": 12,
              "logs": [
                {"message":"timeout no banco","severity":"error","service":"pagamentos","kubeconfig":"apiVersion: v1"},
                {"message":"ok","severity":"info"}
              ]
            }
            """;

        using var document = JsonDocument.Parse(Payload.ShapeLogs(json, 1));
        Assert.Equal(2, document.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("returned").GetInt32());
        var log = document.RootElement.GetProperty("logs")[0];
        Assert.Equal("timeout no banco", log.GetProperty("message").GetString());
        Assert.False(log.TryGetProperty("kubeconfig", out _));
    }

    [Fact]
    public void Redige_senha_em_yaml()
    {
        const string yaml = "env:\n  password: super-secreta\n  name: api\n";
        var text = Payload.RedactText(yaml);
        Assert.DoesNotContain("super-secreta", text);
        Assert.Contains("name: api", text);
    }

    [Fact]
    public void Monta_query_sem_valor_vazio()
    {
        Assert.Equal("api/v1/apm/services?runtime=java", ApiQuery.Build("api/v1/apm/services", ("runtime", "java"), ("machine_id", " ")));
    }

    [Fact]
    public void Redige_segredo_em_objeto_aninhado()
    {
        const string json = """{"cluster":{"name":"prod","access_token":"abc","password":"x"}}""";
        var text = Payload.RedactAndLimit(json);
        Assert.DoesNotContain("abc", text);
        Assert.Contains("prod", text);
        Assert.Contains("[redigido]", text);
    }
}

public sealed class ClientTests : IDisposable
{
    private readonly string _tokenPath = Path.Combine(Path.GetTempPath(), "gw-mcp-tests", Guid.NewGuid().ToString("n"), "token.bin");

    [Fact]
    public async Task Login_ldap_e_reutilizado_na_consulta()
    {
        var handler = new RecordingHandler();
        handler.Respond = (request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/ldap/login", StringComparison.Ordinal))
            {
                Assert.Contains("\"provider_id\":5", body);
                Assert.Contains("\"username\":\"user\"", body);
                return Json(HttpStatusCode.OK, """{"access_token":"abc"}""");
            }

            Assert.Equal("Bearer abc", request.Headers.Authorization?.ToString());
            Assert.DoesNotContain("secret", body);
            Assert.Contains("cluster_name", body);
            return Json(HttpStatusCode.OK, """{"logs":[],"total":0}""");
        };

        var client = CreateClient(handler, new TokenStore(_tokenPath));
        await client.QueryLogsAsync(SampleSearch(), CancellationToken.None);

        Assert.Equal("abc", new TokenStore(_tokenPath).TryRead());
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task Token_invalido_dispara_novo_login()
    {
        var store = new TokenStore(_tokenPath);
        store.Save("velho");
        var queries = 0;
        var handler = new RecordingHandler();
        handler.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/auth/me", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{"email":"a@example.com"}""");
            if (path.EndsWith("/auth/ldap/login", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{"access_token":"novo"}""");

            queries++;
            return queries == 1
                ? Json(HttpStatusCode.Unauthorized, """{"detail":"token expirado"}""")
                : Json(HttpStatusCode.OK, """{"logs":[],"total":0}""");
        };

        var client = CreateClient(handler, store);
        var json = await client.QueryLogsAsync(SampleSearch(), CancellationToken.None);

        Assert.Contains("\"total\":0", json);
        Assert.Equal("novo", store.TryRead());
    }

    public void Dispose()
    {
        if (File.Exists(_tokenPath))
            File.Delete(_tokenPath);
    }

    private GuardWatchClient CreateClient(RecordingHandler handler, TokenStore store)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://guardwatch.example/") };
        var settings = new GuardWatchSettings
        {
            BaseUrl = "https://guardwatch.example",
            LdapProviderId = 5,
            Username = "user",
            Password = "secret"
        };
        return new GuardWatchClient(http, settings, store, NullLogger<GuardWatchClient>.Instance);
    }

    private static LogSearch SampleSearch() =>
        new(null, 15, null, null, null, "prod", null, null, null, null, null, true, 10, "desc");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Calls { get; } = [];
        public Func<HttpRequestMessage, string, HttpResponseMessage> Respond { get; set; } =
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri?.AbsolutePath ?? ""));
            return Respond(request, body);
        }
    }
}
