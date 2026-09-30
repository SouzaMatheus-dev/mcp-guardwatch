using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace GuardWatch.Mcp.Tools;

[McpServerToolType]
public sealed class KubernetesTools(GuardWatchClient client, ILogger<KubernetesTools> logger)
{
    [McpServerTool(Name = "list_k8s_clusters"), Description("Lista os clusters Kubernetes conhecidos pelo GuardWatch. O id daqui entra nas outras ferramentas de cluster.")]
    public Task<string> ListClusters(CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.GetK8sClustersAsync(cancellationToken)));

    [McpServerTool(Name = "k8s_snapshot"), Description("Fotografia do cluster: nós, pods e workloads num único retorno, já reduzido.")]
    public Task<string> Snapshot(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.RedactAndLimit(await client.GetK8sSnapshotAsync(cluster_id, cancellationToken), maxArray: 25));

    [McpServerTool(Name = "list_k8s_namespaces"), Description("Lista namespaces de um cluster.")]
    public Task<string> ListNamespaces(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.GetK8sNamespacesAsync(cluster_id, cancellationToken), 80));

    [McpServerTool(Name = "list_k8s_pods"), Description("Lista pods. Informe o namespace quando o cluster for grande.")]
    public Task<string> ListPods(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Namespace. Vazio lista o cluster inteiro, com corte de tamanho.")] string? ns = null,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.GetK8sPodsAsync(cluster_id, ns, cancellationToken), 40));

    [McpServerTool(Name = "list_k8s_deployments"), Description("Lista deployments e a situação das réplicas.")]
    public Task<string> ListDeployments(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.CompactList(await client.GetK8sDeploymentsAsync(cluster_id, cancellationToken), 40));

    [McpServerTool(Name = "k8s_events"), Description("Eventos do cluster. Por padrão esconde eventos Normal e fica com Warning e Error.")]
    public Task<string> Events(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Quando true, omite eventos Normal. Padrão true.")] bool apenas_avisos = true,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () => Payload.FilterEvents(await client.GetK8sEventsAsync(cluster_id, cancellationToken), apenas_avisos));

    [McpServerTool(Name = "k8s_pod_logs"), Description("Logs atuais de um pod. Para histórico e busca, use query_logs.")]
    public Task<string> PodLogs(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Namespace do pod.")] string ns,
        [Description("Nome do pod.")] string pod,
        [Description("Container, se o pod tiver mais de um.")] string? container = null,
        [Description("Linhas finais. Padrão 200. Máximo 1000.")] int tail = 200,
        [Description("Logs do container anterior, depois de um restart.")] bool previous = false,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () =>
            Payload.LimitText(await client.GetK8sPodLogsAsync(cluster_id, ns, pod, container, tail, previous, cancellationToken)));

    [McpServerTool(Name = "k8s_pod_usage"), Description("Uso de CPU e memória de um pod na janela informada.")]
    public Task<string> PodUsage(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Namespace.")] string ns,
        [Description("Nome do pod.")] string pod,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.GetK8sPodUsageAsync(cluster_id, ns, pod, minutes, cancellationToken), maxArray: 60, keepLast: true));

    [McpServerTool(Name = "k8s_node_usage"), Description("Uso de CPU e memória de um nó na janela informada.")]
    public Task<string> NodeUsage(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Nome do nó.")] string node,
        [Description("Minutos para trás. Padrão 60.")] int minutes = 60,
        CancellationToken cancellationToken = default) =>
        ToolRunner.Run(logger, async () =>
            Payload.RedactAndLimit(await client.GetK8sNodeUsageAsync(cluster_id, node, minutes, cancellationToken), maxArray: 60, keepLast: true));

    [McpServerTool(Name = "list_k8s_nodes"), Description("Lista os nós de um cluster e o uso atual quando a API enviar.")]
    public Task<string> ListNodes(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Cluster(cluster_id, "nodes"), cancellationToken, 40);

    [McpServerTool(Name = "list_k8s_services"), Description("Lista Services Kubernetes de um cluster.")]
    public Task<string> ListServices(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Cluster(cluster_id, "services"), cancellationToken, 40);

    [McpServerTool(Name = "list_k8s_ingresses"), Description("Lista Ingresses de um cluster.")]
    public Task<string> ListIngresses(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Cluster(cluster_id, "ingresses"), cancellationToken, 40);

    [McpServerTool(Name = "list_k8s_hpas"), Description("Lista Horizontal Pod Autoscalers e a réplica atual.")]
    public Task<string> ListHpas(
        [Description("Id do cluster.")] string cluster_id,
        CancellationToken cancellationToken = default) =>
        Read.List(client, logger, ApiQuery.Cluster(cluster_id, "hpas"), cancellationToken, 40);

    [McpServerTool(Name = "k8s_pod_yaml"), Description("YAML de um pod. Segredos embutidos são redigidos quando vierem em JSON; YAML puro é cortado por tamanho.")]
    public Task<string> PodYaml(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Namespace.")] string ns,
        [Description("Nome do pod.")] string pod,
        CancellationToken cancellationToken = default) =>
        Read.Text(client, logger, $"{ApiQuery.Cluster(cluster_id, "pods")}/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(pod)}/yaml", cancellationToken);

    [McpServerTool(Name = "k8s_resource_yaml"), Description("YAML de um recurso Kubernetes, como deployment, service ou ingress.")]
    public Task<string> ResourceYaml(
        [Description("Id do cluster.")] string cluster_id,
        [Description("Tipo do recurso, por exemplo deployment, service ou ingress.")] string kind,
        [Description("Namespace.")] string ns,
        [Description("Nome do recurso.")] string name,
        CancellationToken cancellationToken = default) =>
        Read.Text(client, logger, $"{ApiQuery.Cluster(cluster_id, "resources")}/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/yaml", cancellationToken);
}
