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
        Assert.Equal(
            "api/v1/admin/log-standards/samples?window_hours=1&origin=apps&refresh=false&service=identidade-api&criterion=sensitive&env=prod",
            LogStandards.SamplesPath(1, "apps", "identidade-api", "sensitive", "prd"));
        Assert.Equal("api/v1/admin/log-standards/datadog?days=7&refresh=false", LogStandards.DatadogPath(7));
        Assert.Equal("api/v1/admin/log-standards/datadog?days=1&refresh=false", LogStandards.DatadogPath(null));
        Assert.DoesNotContain("window_hours", LogStandards.DatadogPath(7), StringComparison.Ordinal);
        Assert.DoesNotContain("origin", LogStandards.DatadogPath(7), StringComparison.Ordinal);

        Assert.False(LogStandards.TryWindow(12, out var error));
        Assert.Contains("1, 6 ou 24", error);
        Assert.False(LogStandards.TryOrigin("prd", out _, out var originError));
        Assert.Contains("origin", originError);
        Assert.False(LogStandards.TryStatus("vermelho", out _, out var statusError));
        Assert.Contains("status", statusError);
    }

    [Fact]
    public void Amostra_sensivel_apaga_valor_depois_do_rotulo()
    {
        const string json = """
        {
          "criterion": "sensitive",
          "samples": [
            "{\"RenderedMessage\":\"Detalhes: { Code: \\\"1234567890\\\", Agencia: ***\\\"0001\\\", Conta: ***\\\"1234567-8\\\", Usuario: ***\\\"fulano\\\" }.\",\"Properties\":{\"Detalhes\":{\"Code\":\"1234567890\",\"Agencia\":\"***\",\"Conta\":\"***\",\"Usuario\":\"***\"}}}"
          ]
        }
        """;

        var text = LogStandards.ShapeSamples(json, "identidade-api", "sensitive");

        Assert.DoesNotContain("0001", text);
        Assert.DoesNotContain("1234567-8", text);
        Assert.DoesNotContain("fulano", text);
        Assert.DoesNotContain("1234567890", text);
        Assert.Contains("[redigido]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Amostra_vazia_diz_que_a_regra_nao_tem_linha()
    {
        const string json = """{"criterion":"structured","samples":[]}""";
        var text = LogStandards.ShapeSamples(json, "identidade-api", "structured");

        using var document = JsonDocument.Parse(text);
        Assert.Equal("A regra não tem linha na janela.", document.RootElement.GetProperty("note").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("returned").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("body", out _));
    }

    [Fact]
    public void Datadog_le_targets_e_preserva_query()
    {
        var targets = new JsonArray();
        for (var i = 0; i < 21; i++)
        {
            targets.Add(new JsonObject
            {
                ["target"] = new JsonObject
                {
                    ["type"] = "k8s",
                    ["cluster"] = $"CLUSTER/nome-{i}",
                    ["namespace"] = "namespace",
                    ["workload"] = $"workload-{i}"
                },
                ["evidence"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["target"] = new JsonObject
                        {
                            ["type"] = "k8s",
                            ["cluster"] = $"CLUSTER/nome-{i}",
                            ["namespace"] = "namespace",
                            ["workload"] = $"workload-{i}"
                        }
                    },
                    "Agencia: ***\"0001\""
                }
            });
        }

        var spots = new JsonArray();
        for (var i = 0; i < 25; i++)
            spots.Add(new JsonObject { ["name"] = $"spot-{i}" });

        var root = new JsonObject
        {
            ["days"] = 1,
            ["query"] = new JsonObject { ["elapsed_ms"] = 10, ["error"] = "Consulta ao ClickHouse falhou: DatabaseError" },
            ["summary"] = new JsonObject { ["targets"] = 200, ["apps"] = 185 },
            ["targets"] = targets,
            ["blind_spots"] = spots
        };

        var text = LogStandards.ShapeAudit(root.ToJsonString(), null, null, null, null, 0, "", days: null, datadog: true);
        using var document = JsonDocument.Parse(text);
        var body = document.RootElement;
        Assert.Equal(1, body.GetProperty("days").GetInt32());
        Assert.Equal(200, body.GetProperty("summary").GetProperty("targets").GetInt32());
        Assert.Equal("Consulta ao ClickHouse falhou: DatabaseError", body.GetProperty("query").GetProperty("error").GetString());
        Assert.Equal(21, body.GetProperty("targets_total").GetInt32());
        Assert.Equal(20, body.GetProperty("targets").GetArrayLength());
        var first = body.GetProperty("targets")[0];
        Assert.Equal("k8s", first.GetProperty("type").GetString());
        Assert.Equal("CLUSTER/nome-0", first.GetProperty("cluster").GetString());
        Assert.Equal("namespace", first.GetProperty("namespace").GetString());
        Assert.Equal("workload-0", first.GetProperty("workload").GetString());
        Assert.Equal("k8s", first.GetProperty("evidence")[0].GetProperty("type").GetString());
        Assert.False(body.TryGetProperty("body", out _));
        Assert.False(body.TryGetProperty("services", out _));
        Assert.Equal(25, body.GetProperty("blind_spots_total").GetInt32());
        Assert.Equal(20, body.GetProperty("blind_spots").GetArrayLength());
        Assert.DoesNotContain("0001", text);
    }

    [Fact]
    public void Audit_le_checks_objeto_mapeia_prd_e_omite_baixo_volume()
    {
        const string json = """
        {
          "summary": {"critical_services": 32, "sensitive_services": 32},
          "services": [
            {
              "service": "identidade-api",
              "env": "prod",
              "namespace": "apps",
              "status": "critico",
              "low_volume": false,
              "checks": {
                "structured": {"status": "ok", "ratio": 0.99},
                "sensitive": {"status": "critico", "ok": false, "pct": 0.02, "sample": "cpf 123.456.789-00"},
                "noise": "padronizado",
                "traces": true
              }
            },
            {
              "service": "identidade-api",
              "env": "prod",
              "namespace": "batch",
              "status": "parcial",
              "low_volume": true,
              "checks": {"sensitive": {"status": "ok"}}
            },
            {
              "service": "identidade-api",
              "env": "hml",
              "namespace": "apps",
              "status": "critico",
              "low_volume": false,
              "checks": {"sensitive": "critico"}
            }
          ]
        }
        """;

        var focused = LogStandards.ShapeAudit(json, "identidade-api", "prd", null, null, 1, "apps");
        using var focusedDoc = JsonDocument.Parse(focused);
        Assert.Equal(1, focusedDoc.RootElement.GetProperty("matched").GetInt32());
        Assert.Equal("prod", focusedDoc.RootElement.GetProperty("env").GetString());
        Assert.Equal("apps", focusedDoc.RootElement.GetProperty("services")[0].GetProperty("namespace").GetString());
        var checks = focusedDoc.RootElement.GetProperty("services")[0].GetProperty("checks");
        Assert.Equal("critico", checks.GetProperty("sensitive").GetProperty("status").GetString());
        Assert.Equal(0.99, checks.GetProperty("structured").GetProperty("ratio").GetDouble());
        Assert.Equal(0.02, checks.GetProperty("sensitive").GetProperty("pct").GetDouble());
        Assert.Equal("padronizado", checks.GetProperty("noise").GetString());
        Assert.True(checks.GetProperty("traces").GetBoolean());
        Assert.DoesNotContain("123.456.789-00", focused);
        Assert.Contains("***.***.***-**", focused, StringComparison.Ordinal);

        var listed = LogStandards.ShapeAudit(json, null, "prd", "critico", "sensitive", 1, "apps");
        using var listedDoc = JsonDocument.Parse(listed);
        Assert.Equal(1, listedDoc.RootElement.GetProperty("matched").GetInt32());
        Assert.Equal("critico", listedDoc.RootElement.GetProperty("services")[0].GetProperty("checks").GetProperty("sensitive").GetString());
        Assert.DoesNotContain("123.456.789-00", listed);
        Assert.DoesNotContain("batch", listed, StringComparison.Ordinal);

        var withLowVolume = LogStandards.ShapeAudit(json, "identidade-api", "prod", null, null, 1, "apps", includeLowVolume: true);
        using var lowDoc = JsonDocument.Parse(withLowVolume);
        Assert.Equal(2, lowDoc.RootElement.GetProperty("matched").GetInt32());
        Assert.Equal("apps", lowDoc.RootElement.GetProperty("services")[0].GetProperty("namespace").GetString());
        Assert.Equal("batch", lowDoc.RootElement.GetProperty("services")[1].GetProperty("namespace").GetString());
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
