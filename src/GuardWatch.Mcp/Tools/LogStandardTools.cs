using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class LogStandardTools(GuardWatchClient client, ILogger<LogStandardTools> logger)
{
    [McpServerTool(Name = "log_standards_criteria"), Description("Regras vigentes do padrão de log. A nota da tela usa as seis primeiras: structured, severity, correlation, exceptions, noise e sensitive. traces e datadog são critérios à parte, em summary.criteria_ok do audit.")]
    public Task<string> Criteria(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/admin/log-standards/criteria", cancellationToken);

    [McpServerTool(Name = "log_standards_audit"), Description("Nota do padrão de log v1 por serviço. A nota da tela usa structured, severity, correlation, exceptions, noise e sensitive. OpenTelemetry e Datadog ficam em summary.criteria_ok. Com service, devolve o checks completo. Sem service, devolve o summary, a contagem por status e o mapa id para status, sem linha de log. Omite low_volume por padrão. prd e prod são o mesmo ambiente.")]
    public Task<string> Audit(
        [Description("Janela em horas. Aceita 1, 6 ou 24. Padrão 24.")] int window_hours = 24,
        [Description("Origem: apps, k8s, container, agent ou all. Padrão all.")] string origin = "all",
        [Description("Nome do serviço. Com env, devolve o checks completo, inclusive a evidência mascarada. Não junta linhas do mesmo nome.")] string? service = null,
        [Description("Ambiente da linha do audit. prd e prod viram prod; preprd vira preprod; hml e dev permanecem. list_environments devolve prd, e a linha de produção do audit é prod. Outro valor é comparado cru.")] string? env = null,
        [Description("padronizado, parcial, nao_padronizado ou critico.")] string? status = null,
        [Description("Regra: structured, severity, correlation, exceptions, noise, sensitive, traces ou datadog. Entra quando existe e o status não é ok, pass, passed, padronizado nem true.")] string? rule = null,
        [Description("Quando true, inclui linhas com low_volume. Padrão false, como a tela que oculta baixo volume.")] bool include_low_volume = false,
        CancellationToken cancellationToken = default)
    {
        if (!Ready(window_hours, origin, status, out var safeOrigin, out var error))
            return Task.FromResult(error);

        var path = LogStandards.AuditPath(window_hours, safeOrigin);
        return ToolRunner.Run(logger, async () =>
            LogStandards.ShapeAudit(await client.ReadJsonAsync(path, cancellationToken), service, env, status, rule, window_hours, safeOrigin, include_low_volume));
    }

    [McpServerTool(Name = "log_standards_datadog"), Description("Aba Saída do Datadog. A URL leva days e refresh=false. Não envia window_hours nem origin. Se days vier vazio ou a API recusar o valor, a resposta é o erro do GuardWatch.")]
    public Task<string> Datadog(
        [Description("Dias da aba Datadog. O nome na URL é days. Vazio ou recusado pela API volta o 422 do GuardWatch, sem montar window_hours.")] int? days = null,
        [Description("Nome do serviço, quando a resposta listar serviços.")] string? service = null,
        [Description("Ambiente da linha, se a resposta listar serviços. prd e prod viram prod; preprd vira preprod; hml e dev permanecem.")] string? env = null,
        [Description("Quando true, inclui linhas com low_volume. Padrão false.")] bool include_low_volume = false,
        CancellationToken cancellationToken = default)
    {
        var path = LogStandards.DatadogPath(days);
        return ToolRunner.Run(logger, async () =>
            LogStandards.ShapeAudit(await client.ReadJsonAsync(path, cancellationToken), service, env, null, null, 0, "", include_low_volume, days, datadog: true));
    }

    [McpServerTool(Name = "log_standards_samples"), Description("Linhas que falharam uma regra do padrão de log. service e rule são obrigatórios. A URL envia criterion com o id da regra. Na regra sensitive a linha volta mascarada, sem CPF, CNPJ, conta ou credencial.")]
    public Task<string> Samples(
        [Description("Nome do serviço.")] string service,
        [Description("Regra que falhou. O argumento se chama rule; a URL usa criterion. Ids: structured, severity, correlation, exceptions, noise, sensitive, traces ou datadog.")] string rule,
        [Description("Janela em horas. Aceita 1, 6 ou 24. Padrão 24.")] int window_hours = 24,
        [Description("Origem: apps, k8s, container, agent ou all. Padrão all.")] string origin = "all",
        [Description("Ambiente da linha. prd e prod viram prod; preprd vira preprod; hml e dev permanecem.")] string? env = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(rule))
            return Task.FromResult("Erro: informe service e rule. A amostra sem os dois puxa linha demais.");
        if (!Ready(window_hours, origin, null, out var safeOrigin, out var error))
            return Task.FromResult(error);

        var path = LogStandards.SamplesPath(window_hours, safeOrigin, service.Trim(), rule.Trim(), env);
        return ToolRunner.Run(logger, async () =>
            LogStandards.ShapeSamples(await client.ReadJsonAsync(path, cancellationToken), service.Trim(), rule.Trim()));
    }

    private static bool Ready(int windowHours, string? origin, string? status, out string safeOrigin, out string error)
    {
        safeOrigin = "all";
        if (!LogStandards.TryWindow(windowHours, out var windowError))
        {
            error = "Erro: " + windowError;
            return false;
        }

        if (!LogStandards.TryOrigin(origin, out safeOrigin, out var originError))
        {
            error = "Erro: " + originError;
            return false;
        }

        if (!LogStandards.TryStatus(status, out _, out var statusError))
        {
            error = "Erro: " + statusError;
            return false;
        }

        error = "";
        return true;
    }
}
