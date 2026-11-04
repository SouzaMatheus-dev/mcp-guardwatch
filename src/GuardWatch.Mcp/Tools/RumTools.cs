using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class RumTools(GuardWatchClient client, ILogger<RumTools> logger)
{
    [McpServerTool(Name = "list_rum_applications"), Description("Aplicações de RUM. O id entra em rum_overview, rum_errors, rum_sessions e rum_web_vitals.")]
    public Task<string> ListApplications(CancellationToken cancellationToken = default) =>
        Read.List(client, logger, "api/v1/rum/applications", cancellationToken, 40);

    [McpServerTool(Name = "rum_overview"), Description("Visão de uma aplicação RUM: uso, erro e sessão no recorte que a tela mostra.")]
    public Task<string> Overview(
        [Description("Id da aplicação, obtido em list_rum_applications.")] string application_id,
        CancellationToken cancellationToken = default) =>
        Application(client, logger, application_id, "overview", list: false, cancellationToken);

    [McpServerTool(Name = "rum_errors"), Description("Erros de uma aplicação RUM.")]
    public Task<string> Errors(
        [Description("Id da aplicação RUM.")] string application_id,
        CancellationToken cancellationToken = default) =>
        Application(client, logger, application_id, "errors", list: true, cancellationToken);

    [McpServerTool(Name = "rum_sessions"), Description("Sessões de uma aplicação RUM.")]
    public Task<string> Sessions(
        [Description("Id da aplicação RUM.")] string application_id,
        CancellationToken cancellationToken = default) =>
        Application(client, logger, application_id, "sessions", list: true, cancellationToken);

    [McpServerTool(Name = "rum_web_vitals"), Description("Web Vitals de uma aplicação RUM.")]
    public Task<string> WebVitals(
        [Description("Id da aplicação RUM.")] string application_id,
        CancellationToken cancellationToken = default) =>
        Application(client, logger, application_id, "web-vitals", list: false, cancellationToken);

    private static Task<string> Application(GuardWatchClient client, ILogger logger, string applicationId, string resource, bool list, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            return Task.FromResult("Erro: informe application_id de list_rum_applications.");

        var path = $"api/v1/rum/applications/{Uri.EscapeDataString(applicationId.Trim())}/{resource}";
        return list
            ? Read.List(client, logger, path, cancellationToken, 40)
            : Read.Json(client, logger, path, cancellationToken);
    }
}
