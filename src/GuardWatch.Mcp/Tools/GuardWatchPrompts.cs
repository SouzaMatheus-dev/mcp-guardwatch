using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerPromptType]
public sealed class GuardWatchPrompts
{
    [McpServerPrompt(Name = "investigar_servico"), Description("Roteiro para achar erro e saturação de um serviço no GuardWatch.")]
    public static string Investigar(
        [Description("Nome do serviço, namespace, pod ou máquina.")] string alvo) =>
        $"""
        Investigue '{alvo}' no GuardWatch usando só as ferramentas deste servidor.
        1. log_facets e log_patterns na última hora, filtrando o alvo.
        2. query_logs com severity error e o nome do alvo.
        3. Se for Kubernetes, list_k8s_clusters, list_k8s_pods e k8s_events. Se houver restart, k8s_pod_logs com previous=true.
        4. k8s_pod_usage ou machine_metrics para ver se CPU ou memória acompanhou o erro.
        Responda com a causa mais provável, o horário em que começou e o próximo passo.
        """;
}
