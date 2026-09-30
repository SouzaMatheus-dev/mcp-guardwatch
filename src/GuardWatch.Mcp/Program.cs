using System.Runtime.Versioning;
using GuardWatch.Mcp;
using GuardWatch.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: SupportedOSPlatform("windows")]

if (args.Contains("--check"))
{
    Environment.ExitCode = await Smoke.RunAsync();
    return;
}

var investigateAt = Array.IndexOf(args, "--investigate");
if (investigateAt >= 0)
{
    var term = investigateAt + 1 < args.Length ? args[investigateAt + 1] : "";
    if (string.IsNullOrWhiteSpace(term))
    {
        Console.Error.WriteLine("Informe o termo: --investigate <serviço, namespace ou pod>");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await Smoke.InvestigateAsync(term);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

var settings = GuardWatchSettings.Load();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TokenStore.CreateDefault());
builder.Services.AddSingleton(sp =>
{
    var http = new HttpClient
    {
        BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(90)
    };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("McpGuardWatch/0.3.2");
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    http.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
    return new GuardWatchClient(
        http,
        settings,
        sp.GetRequiredService<TokenStore>(),
        sp.GetRequiredService<ILogger<GuardWatchClient>>());
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInstructions =
            "PRD, HML e DEV convivem na mesma instância do GuardWatch. " +
            "Antes de analisar, chame list_environments sem filtro e mostre a lista com nome, env e sufixo. " +
            "Pergunte se a pessoa quer PRD, HML ou DEV. Não consulte e não escolha por ela. " +
            "Depois da resposta, chame list_environments com esse tier e mostre só os ambientes cujo env e sufixo batem. " +
            "As consultas seguintes usam o env dessa lista e, no Kubernetes, só o cluster do mesmo sufixo.";
    })
    .WithStdioServerTransport()
    .WithTools<EnvironmentTools>()
    .WithTools<LogTools>()
    .WithTools<MetricTools>()
    .WithTools<KubernetesTools>()
    .WithTools<AlertTools>()
    .WithTools<HealthTools>()
    .WithTools<ApmTools>()
    .WithTools<DbmTools>()
    .WithTools<SiemTools>()
    .WithTools<RcaTools>()
    .WithPrompts<GuardWatchPrompts>();

await builder.Build().RunAsync();
