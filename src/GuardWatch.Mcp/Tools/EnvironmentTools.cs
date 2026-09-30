using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class EnvironmentTools(GuardWatchClient client, ILogger<EnvironmentTools> logger)
{
    [McpServerTool(Name = "list_environments"), Description("Lista os ambientes da instância com env e sufixo. Sem tier, mostre a lista e pergunte PRD, HML ou DEV. Com tier, devolve só os ambientes desse recorte, já com env e sufixo.")]
    public Task<string> List(
        [Description("PRD, HML ou DEV. Vazio devolve o catálogo inteiro para a pessoa escolher.")] string? tier = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () =>
        {
            var catalog = EnvironmentCatalog.Read(await client.ReadJsonAsync("api/v1/logs/filter-options", cancellationToken));
            if (string.IsNullOrWhiteSpace(tier))
            {
                return JsonSerializer.Serialize(new
                {
                    ask = "PRD, HML ou DEV",
                    environments = catalog.Select(Present)
                }, JsonOptions.Indented);
            }

            var wanted = EnvironmentCatalog.NormalizeTier(tier);
            if (wanted is null)
                return "Erro: o ambiente precisa ser PRD, HML ou DEV.";

            var matches = EnvironmentCatalog.Matching(catalog, wanted);
            return JsonSerializer.Serialize(new
            {
                tier = wanted,
                environments = matches.Select(Present)
            }, JsonOptions.Indented);
        });

    private static object Present(GuardWatchEnvironment environment) => new
    {
        name = environment.Name,
        env = string.IsNullOrWhiteSpace(environment.Env) ? environment.Tier : environment.Env,
        suffix = environment.Suffix,
        tier = environment.Tier
    };
}
