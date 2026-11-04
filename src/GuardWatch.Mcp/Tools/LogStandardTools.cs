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

    [McpServerTool(Name = "log_standards_audit"), Description("Nota do padrão de log v1 por serviço. A nota da tela usa structured, severity, correlation, exceptions, noise e sensitive. OpenTelemetry e Datadog ficam em summary.criteria_ok. Informe service e env para ver o checks daquele serviço. Sem service, devolve o summary, a contagem por status e uma amostra. O corte genérico esconderia a nota.")]
    public Task<string> Audit(
        [Description("Janela em horas. Aceita 1, 6 ou 24. Padrão 24.")] int window_hours = 24,
        [Description("Origem: apps, k8s, container, agent ou all. Padrão all.")] string origin = "all",
        [Description("Nome do serviço. Com env, devolve o checks completo desse serviço.")] string? service = null,
        [Description("env de list_environments, depois que a pessoa escolheu PRD, HML ou DEV.")] string? env = null,
        [Description("padronizado, parcial, nao_padronizado ou critico.")] string? status = null,
        [Description("Regra: structured, severity, correlation, exceptions, noise, sensitive, traces ou datadog.")] string? rule = null,
        CancellationToken cancellationToken = default)
    {
        if (!Ready(window_hours, origin, status, out var safeOrigin, out var error))
            return Task.FromResult(error);

        var path = LogStandards.AuditPath(window_hours, safeOrigin);
        return ToolRunner.Run(logger, async () =>
            LogStandards.ShapeAudit(await client.ReadJsonAsync(path, cancellationToken), service, env, status, rule, window_hours, safeOrigin));
    }

    [McpServerTool(Name = "log_standards_datadog"), Description("Aba Saída do Datadog do padrão de log, na mesma janela do audit. refresh fica false.")]
    public Task<string> Datadog(
        [Description("Janela em horas. Aceita 1, 6 ou 24. Padrão 24.")] int window_hours = 24,
        [Description("Origem: apps, k8s, container, agent ou all. Padrão all.")] string origin = "all",
        [Description("Nome do serviço, quando a aba listar serviços.")] string? service = null,
        [Description("env de list_environments.")] string? env = null,
        CancellationToken cancellationToken = default)
    {
        if (!Ready(window_hours, origin, null, out var safeOrigin, out var error))
            return Task.FromResult(error);

        var path = LogStandards.DatadogPath(window_hours, safeOrigin);
        return ToolRunner.Run(logger, async () =>
            LogStandards.ShapeAudit(await client.ReadJsonAsync(path, cancellationToken), service, env, null, null, window_hours, safeOrigin));
    }

    [McpServerTool(Name = "log_standards_samples"), Description("Linhas que falharam uma regra do padrão de log. service e rule são obrigatórios. Na regra sensitive a linha volta mascarada, sem CPF, CNPJ, conta ou credencial.")]
    public Task<string> Samples(
        [Description("Nome do serviço.")] string service,
        [Description("Regra que falhou: structured, severity, correlation, exceptions, noise, sensitive, traces ou datadog.")] string rule,
        [Description("Janela em horas. Aceita 1, 6 ou 24. Padrão 24.")] int window_hours = 24,
        [Description("Origem: apps, k8s, container, agent ou all. Padrão all.")] string origin = "all",
        [Description("env de list_environments.")] string? env = null,
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
