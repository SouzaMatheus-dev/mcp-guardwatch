using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class AlertTools(GuardWatchClient client, ILogger<AlertTools> logger)
{
    [McpServerTool(Name = "list_alerts"), Description("Lista alertas visíveis para o usuário.")]
    public Task<string> ListAlerts(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/alerts", cancellationToken, 40);

    [McpServerTool(Name = "alert_analytics"), Description("Analítica de alertas nos últimos dias.")]
    public Task<string> AlertAnalytics(
        [Description("Dias para trás. Padrão 7.")] int days = 7,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/alerts/analytics?days={Math.Clamp(days, 1, 90)}", cancellationToken);

    [McpServerTool(Name = "list_alert_rules"), Description("Regras de alerta configuradas.")]
    public Task<string> ListAlertRules(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/alert-rules", cancellationToken, 40);

    [McpServerTool(Name = "alert_rule_metrics"), Description("Métricas de domínio usadas pelas regras de alerta.")]
    public Task<string> AlertRuleMetrics(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/alert-rules/domain-metrics", cancellationToken);

    [McpServerTool(Name = "list_escalation_policies"), Description("Políticas de escalonamento de alerta.")]
    public Task<string> ListEscalationPolicies(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/escalation-policies", cancellationToken, 40);
}

[McpServerToolType]
public sealed class HealthTools(GuardWatchClient client, ILogger<HealthTools> logger)
{
    [McpServerTool(Name = "system_health"), Description("Saúde dos componentes internos do GuardWatch.")]
    public Task<string> SystemHealth(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/health/components", cancellationToken);

    [McpServerTool(Name = "list_health_checks"), Description("Health checks configurados.")]
    public Task<string> ListHealthChecks(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/health-checks", cancellationToken, 50);

    [McpServerTool(Name = "health_check_results"), Description("Resultados recentes dos health checks.")]
    public Task<string> HealthCheckResults(
        [Description("Id do health check. Vazio lista todos.")] string? health_check_id = null,
        [Description("Status, por exemplo up ou down.")] string? status = null,
        [Description("Quantidade. Padrão 40.")] int limit = 40,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Build("api/v1/health-checks/results",
            ("health_check_id", health_check_id),
            ("status", status),
            ("limit", Math.Clamp(limit, 1, 100).ToString())), cancellationToken, Math.Clamp(limit, 1, 100));

    [McpServerTool(Name = "health_check_uptime"), Description("Uptime de um health check.")]
    public Task<string> HealthCheckUptime(
        [Description("Id do health check.")] string health_check_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/health-checks/{Uri.EscapeDataString(health_check_id)}/uptime", cancellationToken);
}

[McpServerToolType]
public sealed class ApmTools(GuardWatchClient client, ILogger<ApmTools> logger)
{
    [McpServerTool(Name = "apm_overview"), Description("Visão geral de APM: serviços mais lentos e com erro.")]
    public Task<string> Overview(
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        [Description("Id do serviço APM.")] string? service_id = null,
        [Description("Runtime, por exemplo java ou dotnet.")] string? runtime = null,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, ApiQuery.Build("api/v1/apm/overview",
            ("minutes", Math.Clamp(minutes, 1, 1440).ToString()),
            ("service_id", service_id),
            ("runtime", runtime)), cancellationToken);

    [McpServerTool(Name = "list_apm_services"), Description("Serviços com telemetria APM e estatísticas.")]
    public Task<string> ListServices(
        [Description("Id da máquina.")] string? machine_id = null,
        [Description("Runtime.")] string? runtime = null,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Build("api/v1/apm/services",
            ("machine_id", machine_id),
            ("include_stats", "true"),
            ("runtime", runtime)), cancellationToken, 40);

    [McpServerTool(Name = "apm_traces"), Description("Traces de um serviço. Use has_error para achar falhas e name_search para a operação.")]
    public Task<string> Traces(
        [Description("Id do serviço APM.")] string? service_id = null,
        [Description("Status do trace.")] string? status = null,
        [Description("Quando true, só traces com erro.")] bool? has_error = null,
        [Description("Trecho do nome da operação.")] string? name_search = null,
        [Description("Método HTTP.")] string? http_method = null,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = TimeWindow.Resolve(minutes, null, null);
        return Read.List(client, logger, ApiQuery.Build("api/v1/apm/traces",
            ("service_id", service_id),
            ("status", status),
            ("has_error", has_error?.ToString().ToLowerInvariant()),
            ("name_search", name_search),
            ("http_method", http_method),
            ("start_time", start),
            ("end_time", end)), cancellationToken, 30);
    }

    [McpServerTool(Name = "apm_trace"), Description("Detalhe de um trace, com os spans.")]
    public Task<string> Trace(
        [Description("Id do trace.")] string trace_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/apm/traces/{Uri.EscapeDataString(trace_id)}", cancellationToken, 80);

    [McpServerTool(Name = "apm_stats"), Description("Estatísticas de latência e erro do APM.")]
    public Task<string> Stats(
        [Description("Id do serviço APM.")] string? service_id = null,
        [Description("Runtime.")] string? runtime = null,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = TimeWindow.Resolve(minutes, null, null);
        return Read.Json(client, logger, ApiQuery.Build("api/v1/apm/stats",
            ("service_id", service_id),
            ("runtime", runtime),
            ("start_time", start),
            ("end_time", end)), cancellationToken);
    }

    [McpServerTool(Name = "apm_error_groups"), Description("Grupos de erro do APM na janela.")]
    public Task<string> ErrorGroups(
        [Description("Id do serviço APM.")] string? service_id = null,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = TimeWindow.Resolve(minutes, null, null);
        return Read.List(client, logger, ApiQuery.Build("api/v1/apm/error-groups",
            ("service_id", service_id),
            ("start_time", start),
            ("end_time", end),
            ("limit", "30")), cancellationToken, 30);
    }

    [McpServerTool(Name = "apm_runtime_metrics"), Description("Métricas de runtime de um serviço APM, como heap e threads.")]
    public Task<string> RuntimeMetrics(
        [Description("Id do serviço APM.")] string service_id,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = TimeWindow.Resolve(minutes, null, null);
        return Read.Json(client, logger, ApiQuery.Build($"api/v1/apm/services/{Uri.EscapeDataString(service_id)}/runtime-metrics",
            ("start_time", start),
            ("end_time", end),
            ("limit", "60")), cancellationToken, 60, keepLast: true);
    }

    [McpServerTool(Name = "apm_service_map"), Description("Mapa de dependências entre serviços.")]
    public Task<string> ServiceMap(
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/apm/service-map", cancellationToken, 40);
}

[McpServerToolType]
public sealed class DbmTools(GuardWatchClient client, ILogger<DbmTools> logger)
{
    [McpServerTool(Name = "dbm_overview"), Description("Visão geral do monitoramento de banco.")]
    public Task<string> Overview(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/dbm/overview", cancellationToken);

    [McpServerTool(Name = "list_dbm_connections"), Description("Conexões de banco monitoradas.")]
    public Task<string> ListConnections(
        [Description("Tipo, por exemplo postgresql.")] string? db_type = null,
        [Description("Status da conexão.")] string? status = null,
        [Description("Id da máquina.")] string? machine_id = null,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Build("api/v1/dbm/connections",
            ("db_type", db_type), ("status", status), ("machine_id", machine_id)), cancellationToken, 40);

    [McpServerTool(Name = "dbm_connection"), Description("Detalhe de uma conexão de banco, sem segredo de credencial.")]
    public Task<string> Connection(
        [Description("Id da conexão.")] string connection_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/dbm/connections/{Uri.EscapeDataString(connection_id)}", cancellationToken);

    [McpServerTool(Name = "dbm_query_metrics"), Description("Consultas mais caras de uma conexão.")]
    public Task<string> QueryMetrics(
        [Description("Id da conexão.")] string connection_id,
        [Description("Horas para trás. Padrão 6.")] int hours = 6,
        [Description("Quantidade. Padrão 20.")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, MetricsPath(connection_id, "query-metrics", hours, limit), cancellationToken, Math.Clamp(limit, 1, 50));

    [McpServerTool(Name = "dbm_query_metrics_aggregated"), Description("Consultas agregadas por texto normalizado.")]
    public Task<string> QueryMetricsAggregated(
        [Description("Id da conexão.")] string connection_id,
        [Description("Horas para trás. Padrão 6.")] int hours = 6,
        [Description("Quantidade. Padrão 20.")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, MetricsPath(connection_id, "query-metrics/aggregated", hours, limit), cancellationToken, Math.Clamp(limit, 1, 50));

    [McpServerTool(Name = "dbm_host_metrics"), Description("CPU, memória e I/O do host de banco.")]
    public Task<string> HostMetrics(
        [Description("Id da conexão.")] string connection_id,
        [Description("Horas para trás. Padrão 6.")] int hours = 6,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, ApiQuery.Build($"api/v1/dbm/connections/{Uri.EscapeDataString(connection_id)}/host-metrics",
            ("hours", Math.Clamp(hours, 1, 168).ToString()),
            ("limit", "60")), cancellationToken, 60, keepLast: true);

    [McpServerTool(Name = "dbm_active_sessions"), Description("Sessões ativas no banco.")]
    public Task<string> ActiveSessions(
        [Description("Id da conexão.")] string connection_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, $"api/v1/dbm/connections/{Uri.EscapeDataString(connection_id)}/active-sessions", cancellationToken, 40);

    [McpServerTool(Name = "dbm_database_stats"), Description("Estatísticas por database.")]
    public Task<string> DatabaseStats(
        [Description("Id da conexão.")] string connection_id,
        [Description("Horas para trás. Padrão 6.")] int hours = 6,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, ApiQuery.Build($"api/v1/dbm/connections/{Uri.EscapeDataString(connection_id)}/database-stats",
            ("hours", Math.Clamp(hours, 1, 168).ToString())), cancellationToken);

    [McpServerTool(Name = "dbm_table_stats"), Description("Tabelas com mais leitura, escrita ou tamanho.")]
    public Task<string> TableStats(
        [Description("Id da conexão.")] string connection_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, $"api/v1/dbm/connections/{Uri.EscapeDataString(connection_id)}/table-stats", cancellationToken, 30);

    [McpServerTool(Name = "dbm_lock_events"), Description("Eventos de lock da conexão.")]
    public Task<string> LockEvents(
        [Description("Id da conexão.")] string connection_id,
        [Description("Horas para trás. Padrão 6.")] int hours = 6,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Build($"api/v1/dbm/connections/{Uri.EscapeDataString(connection_id)}/lock-events",
            ("hours", Math.Clamp(hours, 1, 168).ToString())), cancellationToken, 30);

    private static string MetricsPath(string connectionId, string resource, int hours, int limit) =>
        ApiQuery.Build($"api/v1/dbm/connections/{Uri.EscapeDataString(connectionId)}/{resource}",
            ("hours", Math.Clamp(hours, 1, 168).ToString()),
            ("limit", Math.Clamp(limit, 1, 50).ToString()));
}

[McpServerToolType]
public sealed class SiemTools(GuardWatchClient client, ILogger<SiemTools> logger)
{
    [McpServerTool(Name = "siem_overview"), Description("Resumo do SIEM.")]
    public Task<string> Overview(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/siem/overview", cancellationToken);

    [McpServerTool(Name = "siem_events"), Description("Eventos de segurança recentes.")]
    public Task<string> Events(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/siem/events", cancellationToken, 30);

    [McpServerTool(Name = "siem_event"), Description("Detalhe de um evento de segurança.")]
    public Task<string> Event(
        [Description("Id do evento.")] string event_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/siem/events/{Uri.EscapeDataString(event_id)}", cancellationToken);

    [McpServerTool(Name = "siem_incidents"), Description("Incidentes de segurança.")]
    public Task<string> Incidents(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/siem/incidents", cancellationToken, 30);

    [McpServerTool(Name = "siem_incident_logs"), Description("Logs ligados a um incidente.")]
    public Task<string> IncidentLogs(
        [Description("Id do incidente.")] string incident_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/siem/incidents/{Uri.EscapeDataString(incident_id)}/logs", cancellationToken, 40);

    [McpServerTool(Name = "siem_rules"), Description("Regras de detecção do SIEM.")]
    public Task<string> Rules(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/siem/rules", cancellationToken, 40);

    [McpServerTool(Name = "siem_event_trend"), Description("Tendência de eventos de segurança.")]
    public Task<string> EventTrend(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/siem/dashboard/event-trend", cancellationToken);

    [McpServerTool(Name = "siem_response_metrics"), Description("Métricas de resposta a incidentes.")]
    public Task<string> ResponseMetrics(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/siem/dashboard/response-metrics", cancellationToken);
}

[McpServerToolType]
public sealed class RcaTools(GuardWatchClient client, ILogger<RcaTools> logger)
{
    [McpServerTool(Name = "rca_overview"), Description("Resumo dos casos de causa raiz.")]
    public Task<string> Overview(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/rca/overview", cancellationToken);

    [McpServerTool(Name = "rca_cases"), Description("Casos de RCA visíveis para o usuário.")]
    public Task<string> Cases(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/rca/cases", cancellationToken, 30);

    [McpServerTool(Name = "rca_case"), Description("Detalhe de um caso de RCA.")]
    public Task<string> Case(
        [Description("Id do caso.")] string case_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/rca/cases/{Uri.EscapeDataString(case_id)}", cancellationToken);

    [McpServerTool(Name = "rca_case_events"), Description("Linha do tempo de um caso de RCA.")]
    public Task<string> CaseEvents(
        [Description("Id do caso.")] string case_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, $"api/v1/rca/cases/{Uri.EscapeDataString(case_id)}/events", cancellationToken, 40);
}
