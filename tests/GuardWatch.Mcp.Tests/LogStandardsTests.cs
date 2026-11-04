using System.Text.Json;
using System.Text.Json.Nodes;

namespace GuardWatch.Mcp.Tests;

public sealed class LogStandardsTests
{
    [Fact]
    public void Audit_devolve_o_servico_pedido_mesmo_fora_dos_primeiros()
    {
        var json = AuditFixture(out var total);
        var text = LogStandards.ShapeAudit(json, "api", "hml", null, null, 24, "all");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(total, root.GetProperty("services_total").GetInt32());
        Assert.Equal(1, root.GetProperty("matched").GetInt32());
        Assert.False(root.GetProperty("summary").GetProperty("criteria_ok").GetProperty("datadog").GetBoolean());
        var service = root.GetProperty("services")[0];
        Assert.Equal("api", service.GetProperty("service").GetString());
        Assert.Equal("hml", service.GetProperty("env").GetString());
        var sensitive = service.GetProperty("checks").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "sensitive");
        Assert.Equal("critico", sensitive.GetProperty("status").GetString());
        Assert.Equal(0.7, service.GetProperty("checks").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "structured").GetProperty("ratio").GetDouble());
        Assert.DoesNotContain("123.456.789-00", text);
        Assert.DoesNotContain("abc123secreto", text);
        Assert.Contains("***.***.***-**", text, StringComparison.Ordinal);
        Assert.Contains(LogStandards.Reading, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Audit_sem_servico_resume_e_nao_traz_a_linha_sensivel()
    {
        var text = LogStandards.ShapeAudit(AuditFixture(out var total), null, null, null, null, 6, "apps");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(total, root.GetProperty("matched").GetInt32());
        Assert.Equal(20, root.GetProperty("returned").GetInt32());
        Assert.True(root.GetProperty("by_status").GetProperty("padronizado").GetInt32() > 0);
        Assert.True(root.GetProperty("by_status").GetProperty("parcial").GetInt32() >= 1);
        Assert.DoesNotContain("123.456.789-00", text);
        Assert.DoesNotContain("abc123secreto", text);
    }

    [Fact]
    public void Audit_filtra_status_e_regra()
    {
        var json = AuditFixture(out _);
        var parcial = LogStandards.ShapeAudit(json, null, null, "parcial", null, 1, "k8s");
        var sensitive = LogStandards.ShapeAudit(json, null, null, null, "sensitive", 1, "all");

        using var parcialDoc = JsonDocument.Parse(parcial);
        using var sensitiveDoc = JsonDocument.Parse(sensitive);
        Assert.Equal(1, parcialDoc.RootElement.GetProperty("matched").GetInt32());
        Assert.Equal(1, sensitiveDoc.RootElement.GetProperty("matched").GetInt32());
        Assert.Equal("api", sensitiveDoc.RootElement.GetProperty("services")[0].GetProperty("service").GetString());
    }

    [Fact]
    public void Audit_avisa_quando_a_lista_nao_vem()
    {
        const string json = """{"summary":{"criteria_ok":{"traces":true}},"markers":[1]}""";
        var text = LogStandards.ShapeAudit(json, "api", "prd", null, null, 24, "all");

        using var document = JsonDocument.Parse(text);
        Assert.True(document.RootElement.GetProperty("summary").GetProperty("criteria_ok").GetProperty("traces").GetBoolean());
        Assert.Contains("não trouxe a lista", document.RootElement.GetProperty("note").GetString());
        Assert.Contains("markers", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Amostra_sensivel_mascara_documento_e_ignora_outro_servico()
    {
        const string json = """
        {
          "samples": [
            {"service":"api","rule":"sensitive","message":"cpf 123.456.789-00 conta 1234567 documento 52998224725"},
            {"service":"worker","rule":"sensitive","message":"nao-mostrar"},
            {"service":"api","rule":"noise","message":"health"}
          ]
        }
        """;

        var text = LogStandards.ShapeSamples(json, "api", "sensitive");

        Assert.DoesNotContain("123.456.789-00", text);
        Assert.DoesNotContain("52998224725", text);
        Assert.DoesNotContain("1234567", text);
        Assert.DoesNotContain("nao-mostrar", text);
        Assert.DoesNotContain("health", text);
        Assert.Contains("***.***.***-**", text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        Assert.True(document.RootElement.GetProperty("masked").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("returned").GetInt32());
    }

    [Fact]
    public void Consulta_fixa_refresh_false_e_rejeita_janela()
    {
        var path = LogStandards.AuditPath(24, "all");
        Assert.Equal("api/v1/admin/log-standards/audit?window_hours=24&origin=all&refresh=false", path);
        Assert.DoesNotContain("service=", path, StringComparison.Ordinal);
        Assert.Contains("refresh=false", LogStandards.SamplesPath(1, "apps", "api", "noise", "prd"), StringComparison.Ordinal);

        Assert.False(LogStandards.TryWindow(12, out var error));
        Assert.Contains("1, 6 ou 24", error);
        Assert.False(LogStandards.TryOrigin("prd", out _, out var originError));
        Assert.Contains("origin", originError);
        Assert.False(LogStandards.TryStatus("vermelho", out _, out var statusError));
        Assert.Contains("status", statusError);
    }

    [Fact]
    public void Erp_preserva_summary_e_corta_traces()
    {
        var root = new JsonObject
        {
            ["summary"] = new JsonObject { ["screens"] = 30, ["token"] = "segredo" },
            ["top_screens"] = new JsonArray(),
            ["traces"] = new JsonArray(),
            ["logs"] = new JsonArray()
        };
        for (var i = 0; i < 30; i++)
        {
            ((JsonArray)root["traces"]!).Add(new JsonObject { ["name"] = $"t{i}" });
            ((JsonArray)root["logs"]!).Add(new JsonObject { ["message"] = $"linha {i}" });
        }

        for (var i = 0; i < 5; i++)
            ((JsonArray)root["top_screens"]!).Add(new JsonObject { ["name"] = $"tela {i}" });

        var text = TelemetryShape.Erp(root.ToJsonString());
        using var document = JsonDocument.Parse(text);
        Assert.Equal(30, document.RootElement.GetProperty("summary").GetProperty("screens").GetInt32());
        Assert.Equal(30, document.RootElement.GetProperty("traces_total").GetInt32());
        Assert.Equal(15, document.RootElement.GetProperty("traces").GetArrayLength());
        Assert.Equal(15, document.RootElement.GetProperty("logs").GetArrayLength());
        Assert.Equal(5, document.RootElement.GetProperty("top_screens").GetArrayLength());
        Assert.DoesNotContain("segredo", text);
        Assert.Contains("[redigido]", text, StringComparison.Ordinal);
    }

    private static string AuditFixture(out int total)
    {
        var services = new JsonArray();
        for (var i = 0; i < 60; i++)
        {
            services.Add(new JsonObject
            {
                ["service"] = $"svc-{i}",
                ["env"] = "prd",
                ["status"] = "padronizado",
                ["checks"] = new JsonArray
                {
                    new JsonObject { ["id"] = "structured", ["status"] = "padronizado", ["ratio"] = 0.99 },
                    new JsonObject { ["id"] = "sensitive", ["status"] = "padronizado" }
                }
            });
        }

        services[55] = new JsonObject
        {
            ["service"] = "api",
            ["env"] = "hml",
            ["status"] = "parcial",
            ["checks"] = new JsonArray
            {
                new JsonObject { ["id"] = "structured", ["status"] = "parcial", ["ratio"] = 0.7 },
                new JsonObject { ["id"] = "sensitive", ["status"] = "critico", ["sample"] = "cpf 123.456.789-00 token=abc123secreto" },
                new JsonObject { ["id"] = "traces", ["status"] = "padronizado" },
                new JsonObject { ["id"] = "datadog", ["status"] = "nao_padronizado" }
            }
        };

        total = services.Count;
        var root = new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["criteria_ok"] = new JsonObject { ["traces"] = true, ["datadog"] = false }
            },
            ["services"] = services
        };
        return root.ToJsonString();
    }
}
