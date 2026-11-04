using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class PlatformTools(GuardWatchClient client, ILogger<PlatformTools> logger)
{
    [McpServerTool(Name = "list_endpoint_monitors"), Description("Monitores sintéticos. Cada item traz last_healthy, last_status_code, last_total_ms, sla_target e environment quando a API envia.")]
    public Task<string> EndpointMonitors(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/endpoint-monitors", cancellationToken, 80);

    [McpServerTool(Name = "erp_telemetry"), Description("Telemetria do ERP: summary, telas mais usadas, traces e logs. Traces e logs voltam cortados.")]
    public Task<string> ErpTelemetry(CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => TelemetryShape.Erp(await client.ReadJsonAsync("api/v1/erp/telemetry", cancellationToken)));

    [McpServerTool(Name = "guardwatch_version"), Description("Versão, commit e data de build da instância do GuardWatch. Use antes de citar o padrão de log, para amarrar a leitura ao build.")]
    public Task<string> Version(CancellationToken cancellationToken = default) =>
        Read.Json(client, logger, "api/v1/version", cancellationToken);
}
