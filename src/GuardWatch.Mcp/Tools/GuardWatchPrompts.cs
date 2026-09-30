using System.ComponentModel;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerPromptType]
public sealed class GuardWatchPrompts
{
    [McpServerPrompt(Name = "investigar_servico"), Description("Roteiro para achar erro e saturação de um serviço no GuardWatch. Exige o ambiente: PRD, HML ou DEV.")]
    public static string Investigar(
        [Description("Nome do serviço, namespace, pod ou máquina.")] string alvo,
        [Description("Ambiente da análise: PRD, HML ou DEV.")] string ambiente) =>
        $"""
        A pessoa pediu a análise de '{alvo}' no ambiente {ambiente}.
        PRD, HML e DEV convivem na mesma instância.
        Se {ambiente} não for exatamente PRD, HML ou DEV, pergunte de novo e não chame outra ferramenta até a resposta.
        1. list_environments com tier {ambiente}. Mostre nome, env e sufixo de cada ambiente devolvido antes de seguir.
        2. log_facets e log_patterns na última hora, com o env dessa lista e o alvo.
        3. query_logs com severity error, o mesmo env e o nome do alvo.
        4. Se for Kubernetes, list_k8s_clusters e siga só o cluster com o sufixo de {ambiente}. Depois list_k8s_pods e k8s_events. Se houver restart, k8s_pod_logs com previous=true.
        5. k8s_pod_usage ou machine_metrics para ver se CPU ou memória acompanhou o erro.
        Responda com a causa mais provável, o horário em que começou e o próximo passo. Cite os ambientes usados, com env e sufixo.
        """;
}
