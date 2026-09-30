using Microsoft.Extensions.Logging;

namespace GuardWatch.Mcp.Tools;

internal static class Read
{
    public static Task<string> List(GuardWatchClient client, ILogger logger, string path, CancellationToken cancellationToken, int max = 40) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.ReadJsonAsync(path, cancellationToken), max));

    public static Task<string> Json(GuardWatchClient client, ILogger logger, string path, CancellationToken cancellationToken, int max = 40, bool keepLast = false) =>
        ToolRunner.Run(logger, async () => Payload.RedactAndLimit(await client.ReadJsonAsync(path, cancellationToken), maxArray: max, keepLast: keepLast));

    public static Task<string> Text(GuardWatchClient client, ILogger logger, string path, CancellationToken cancellationToken) =>
        ToolRunner.Run(logger, async () => Payload.RedactText(await client.ReadTextAsync(path, cancellationToken)));
}

internal static class ToolRunner
{
    public static async Task<string> Run(ILogger logger, Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (GuardWatchException ex)
        {
            logger.LogWarning("GuardWatch: {Message}", ex.Message);
            return "Erro: " + ex.Message;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha de rede ao chamar o GuardWatch.");
            return "Erro de rede ao chamar o GuardWatch: " + ex.Message;
        }
        catch (TaskCanceledException)
        {
            return "Erro: a consulta excedeu o tempo limite.";
        }
    }
}
