using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class MetricTools(GuardWatchClient client, ILogger<MetricTools> logger)
{
    [McpServerTool(Name = "whoami"), Description("Mostra o usuário autenticado no GuardWatch. Use para confirmar se o login LDAP funcionou.")]
    public Task<string> WhoAmI(CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.RedactAndLimit(await client.GetMeAsync(cancellationToken)));

    [McpServerTool(Name = "list_machines"), Description("Lista máquinas monitoradas. O id daqui entra em machine_metrics e machine_metrics_history.")]
    public Task<string> ListMachines(CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.GetMachinesAsync(cancellationToken), 60));

    [McpServerTool(Name = "dashboard_summary"), Description("Resumo do dashboard: saúde e uso atual. Pode filtrar por uma máquina.")]
    public Task<string> DashboardSummary(
        [Description("Id da máquina. Vazio para o resumo geral.")] string? machine_id = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.RedactAndLimit(await client.GetDashboardSummaryAsync(machine_id, cancellationToken), maxArray: 20));

    [McpServerTool(Name = "machine_metrics"), Description("Amostras recentes de CPU, memória, disco e rede de uma máquina.")]
    public Task<string> MachineMetrics(
        [Description("Id da máquina, obtido em list_machines.")] string machine_id,
        [Description("Quantidade de amostras. Padrão 8. Máximo 50.")] int limit = 8,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.GetMachineMetricsAsync(machine_id, limit, cancellationToken), maxArray: Math.Clamp(limit, 1, 50), keepLast: true));

    [McpServerTool(Name = "machine_metrics_history"), Description("Histórico de métricas de uma máquina. Use para ver se CPU ou memória subiu junto com o erro.")]
    public Task<string> MachineMetricsHistory(
        [Description("Id da máquina.")] string machine_id,
        [Description("Horas para trás. Padrão 24. Máximo 168.")] int hours = 24,
        [Description("hourly, minute ou raw. Padrão hourly.")] string interval = "hourly",
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.GetMetricsHistoryAsync(machine_id, hours, interval, cancellationToken), maxArray: 48, keepLast: true));

    [McpServerTool(Name = "alert_summary"), Description("Resumo dos alertas nas últimas horas.")]
    public Task<string> AlertSummary(
        [Description("Janela em horas. Padrão 24.")] int hours = 24,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.RedactAndLimit(await client.GetAlertSummaryAsync(hours, cancellationToken)));

    [McpServerTool(Name = "machine_detail"), Description("Cadastro e estado de uma máquina.")]
    public Task<string> MachineDetail(
        [Description("Id da máquina.")] string machine_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/machines/{Uri.EscapeDataString(machine_id)}", cancellationToken);

    [McpServerTool(Name = "machine_uptime"), Description("Disponibilidade recente de uma máquina.")]
    public Task<string> MachineUptime(
        [Description("Id da máquina.")] string machine_id,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/machines/{Uri.EscapeDataString(machine_id)}/uptime", cancellationToken);

    [McpServerTool(Name = "machine_processes"), Description("Processos observados em uma máquina.")]
    public Task<string> MachineProcesses(
        [Description("Id da máquina.")] string machine_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, $"api/v1/machines/{Uri.EscapeDataString(machine_id)}/processes", cancellationToken, 40);

    [McpServerTool(Name = "machine_services"), Description("Serviços de sistema observados em uma máquina.")]
    public Task<string> MachineServices(
        [Description("Id da máquina.")] string machine_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, $"api/v1/machines/{Uri.EscapeDataString(machine_id)}/services", cancellationToken, 40);

    [McpServerTool(Name = "machine_containers"), Description("Containers observados em uma máquina.")]
    public Task<string> MachineContainers(
        [Description("Id da máquina.")] string machine_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, $"api/v1/machines/{Uri.EscapeDataString(machine_id)}/containers", cancellationToken, 40);

    [McpServerTool(Name = "list_containers"), Description("Lista containers. Filtre por host ou serviço Swarm.")]
    public Task<string> ListContainers(
        [Description("Id do host.")] string? host_id = null,
        [Description("Nome do serviço Swarm.")] string? swarm_service = null,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Build("api/v1/containers", ("host_id", host_id), ("swarm_service", swarm_service)), cancellationToken, 40);

    [McpServerTool(Name = "container_metrics"), Description("CPU e memória de um container na janela informada.")]
    public Task<string> ContainerMetrics(
        [Description("Id do host.")] string host_id,
        [Description("Nome do container.")] string container,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, $"api/v1/containers/{Uri.EscapeDataString(host_id)}/{Uri.EscapeDataString(container)}/metrics?minutes={Math.Clamp(minutes, 1, 1440)}", cancellationToken, 60, keepLast: true);

    [McpServerTool(Name = "container_logs"), Description("Logs ingeridos de um container, com busca e severidade.")]
    public Task<string> ContainerLogs(
        [Description("Id do host.")] string host_id,
        [Description("Nome do container.")] string container,
        [Description("Texto a procurar.")] string? search = null,
        [Description("Severidade.")] string? severity = null,
        [Description("Horas para trás. Padrão 6.")] int hours = 6,
        [Description("Quantidade. Padrão 40. Máximo 100.")] int limit = 40,
        CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, ApiQuery.Build(
            $"api/v1/containers/{Uri.EscapeDataString(host_id)}/{Uri.EscapeDataString(container)}/logs",
            ("search", search),
            ("severity", severity),
            ("hours", Math.Clamp(hours, 1, 168).ToString()),
            ("limit", Math.Clamp(limit, 1, 100).ToString())), cancellationToken, Math.Clamp(limit, 1, 100));
}
