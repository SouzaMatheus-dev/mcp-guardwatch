using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class LogTools(GuardWatchClient client, ILogger<LogTools> logger)
{
    private const string EnvHelp = "Ambiente que a pessoa escolheu: PRD, HML ou DEV. Se ela não disse, pergunte antes de chamar.";
    [McpServerTool(Name = "query_logs"), Description("Busca logs do GuardWatch. Use para achar erros, exceções e mensagens de um serviço, namespace ou máquina.")]
    public Task<string> QueryLogs(
        [Description("Texto livre, no mesmo formato da busca da tela de Logs. Ex.: timeout, NullReference, trace id.")] string? query = null,
        [Description("Janela em minutos até agora. Ignorada se start_time e end_time forem informados. Padrão 60. Máximo 10080.")] int minutes = 60,
        [Description("Início em ISO-8601 UTC.")] string? start_time = null,
        [Description("Fim em ISO-8601 UTC.")] string? end_time = null,
        [Description("Id da máquina no GuardWatch.")] string? machine_id = null,
        [Description("Nome do cluster Kubernetes.")] string? cluster_name = null,
        [Description("Namespace Kubernetes.")] string? ns = null,
        [Description("Nome do serviço.")] string? service = null,
        [Description("Severidade: debug, info, warning, error, critical.")] string? severity = null,
        [Description(EnvHelp)] string? env = null,
        [Description("Tipo de erro, quando a tela de logs classifica um.")] string? error_type = null,
        [Description("Remove ruído configurado no GuardWatch. Padrão true.")] bool exclude_noise = true,
        [Description("Quantidade de linhas. Padrão 40. Máximo 100.")] int limit = 40,
        [Description("desc para os mais recentes, asc para os mais antigos.")] string sort = "desc",
        CancellationToken cancellationToken = default)
    {
        var search = new LogSearch(query, minutes, start_time, end_time, machine_id, cluster_name, ns, service, severity, env, error_type, exclude_noise, limit, sort);
        return ToolRunner.Run(logger, async () =>
            Payload.ShapeLogs(await client.QueryLogsAsync(search, cancellationToken), Math.Clamp(limit, 1, 100)));
    }

    [McpServerTool(Name = "log_histogram"), Description("Volume de logs ao longo do tempo. Use para ver quando um erro começou a subir.")]
    public Task<string> LogHistogram(
        [Description("Janela em minutos até agora. Padrão 60.")] int minutes = 60,
        [Description("Início em ISO-8601 UTC.")] string? start_time = null,
        [Description("Fim em ISO-8601 UTC.")] string? end_time = null,
        [Description("Id da máquina.")] string? machine_id = null,
        [Description("Nome do cluster.")] string? cluster_name = null,
        [Description("Namespace.")] string? ns = null,
        [Description("Serviço.")] string? service = null,
        [Description("Severidade.")] string? severity = null,
        [Description(EnvHelp)] string? env = null,
        [Description("Tipo de erro.")] string? error_type = null,
        [Description("Remove ruído. Padrão true.")] bool exclude_noise = true,
        [Description("Número de baldes. Padrão 30.")] int buckets = 30,
        CancellationToken cancellationToken = default)
    {
        var search = new LogSearch(null, minutes, start_time, end_time, machine_id, cluster_name, ns, service, severity, env, error_type, exclude_noise, 40, "desc");
        return ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.LogHistogramAsync(search, buckets, cancellationToken), maxArray: Math.Clamp(buckets, 5, 120)));
    }

    [McpServerTool(Name = "log_patterns"), Description("Agrupa logs parecidos. Use para ver qual mensagem mais se repete na janela.")]
    public Task<string> LogPatterns(
        [Description("Janela em minutos até agora. Padrão 60.")] int minutes = 60,
        [Description("Início em ISO-8601 UTC.")] string? start_time = null,
        [Description("Fim em ISO-8601 UTC.")] string? end_time = null,
        [Description("Id da máquina.")] string? machine_id = null,
        [Description("Serviço.")] string? service = null,
        [Description("Severidade.")] string? severity = null,
        [Description(EnvHelp)] string? env = null,
        [Description("Remove ruído. Padrão true.")] bool exclude_noise = true,
        CancellationToken cancellationToken = default)
    {
        var search = new LogSearch(null, minutes, start_time, end_time, machine_id, null, null, service, severity, env, null, exclude_noise, 40, "desc");
        return ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.LogPatternsAsync(search, cancellationToken), maxArray: 30));
    }

    [McpServerTool(Name = "log_facets"), Description("Conta logs por serviço, severidade, namespace e cluster. Use para descobrir onde está o volume.")]
    public Task<string> LogFacets(
        [Description("Janela em minutos até agora. Padrão 60.")] int minutes = 60,
        [Description("Início em ISO-8601 UTC.")] string? start_time = null,
        [Description("Fim em ISO-8601 UTC.")] string? end_time = null,
        [Description("Id da máquina.")] string? machine_id = null,
        [Description("Nome do cluster.")] string? cluster_name = null,
        [Description("Namespace.")] string? ns = null,
        [Description(EnvHelp)] string? env = null,
        [Description("Remove ruído. Padrão true.")] bool exclude_noise = true,
        CancellationToken cancellationToken = default)
    {
        var search = new LogSearch(null, minutes, start_time, end_time, machine_id, cluster_name, ns, null, null, env, null, exclude_noise, 40, "desc");
        return ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.LogFacetsAsync(search, cancellationToken), maxArray: 25));
    }

    [McpServerTool(Name = "log_stats"), Description("Resumo geral do volume de logs ingerido.")]
    public Task<string> LogStats(CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.RedactAndLimit(await client.GetLogStatsAsync(cancellationToken)));

    [McpServerTool(Name = "list_log_services"), Description("Lista os serviços que estão enviando logs.")]
    public Task<string> ListLogServices(CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.GetLogServicesAsync(cancellationToken)));

    [McpServerTool(Name = "logs_by_trace"), Description("Traz os logs de um trace id.")]
    public Task<string> LogsByTrace(
        [Description("Trace id.")] string trace_id,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.ShapeLogs(await client.GetLogsByTraceAsync(trace_id, cancellationToken), 80));

    [McpServerTool(Name = "log_context"), Description("Linhas vizinhas de um evento de log.")]
    public Task<string> LogContext(
        [Description("Id do evento de log.")] string event_id,
        [Description("Linha âncora, quando a API pedir.")] int? line = null,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, ApiQuery.Build($"api/v1/logs/context/{Uri.EscapeDataString(event_id)}", ("line", line?.ToString())), cancellationToken);

    [McpServerTool(Name = "log_filter_options"), Description("Valores disponíveis para filtrar logs: serviços, severidades, ambientes e clusters.")]
    public Task<string> LogFilterOptions(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/logs/filter-options", cancellationToken);

    [McpServerTool(Name = "log_services_by_machine"), Description("Serviços de log agrupados pela máquina que os enviou.")]
    public Task<string> LogServicesByMachine(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/logs/services-by-machine", cancellationToken, 60);

    [McpServerTool(Name = "log_service_aliases"), Description("Apelidos de serviço usados na busca de logs.")]
    public Task<string> LogServiceAliases(
        [Description("Id da máquina. Vazio lista todos.")] string? machine_id = null,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Build("api/v1/logs/service-aliases", ("machine_id", machine_id)), cancellationToken);

    [McpServerTool(Name = "log_saved_filters"), Description("Filtros de log salvos pelo usuário no GuardWatch.")]
    public Task<string> LogSavedFilters(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/logs/saved-filters", cancellationToken);

    [McpServerTool(Name = "log_index_health"), Description("Atraso da ingestão e saúde do índice de logs no intervalo.")]
    public Task<string> LogIndexHealth(
        [Description("Início em ISO-8601 UTC. Vazio usa a janela padrão da API.")] string? start_time = null,
        [Description("Fim em ISO-8601 UTC.")] string? end_time = null,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, ApiQuery.Build("api/v1/v2/logs/index-health", ("range_start", start_time), ("range_end", end_time)), cancellationToken);
}
