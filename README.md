# GuardWatch MCP

[![NuGet](https://img.shields.io/nuget/v/McpGuardWatch?logo=nuget&logoColor=white&label=NuGet&color=004880)](https://www.nuget.org/packages/McpGuardWatch)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![C#](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![MCP](https://img.shields.io/badge/MCP-2.2-111111)](https://modelcontextprotocol.io)
[![License](https://img.shields.io/github/license/SouzaMatheus-dev/guardwatch-mcp?color=blue)](LICENSE)

![Logs](https://img.shields.io/badge/Logs-leitura-0f766e)
![Métricas](https://img.shields.io/badge/M%C3%A9tricas-leitura-0369a1)
![Kubernetes](https://img.shields.io/badge/Kubernetes-leitura-326CE5?logo=kubernetes&logoColor=white)
![APM](https://img.shields.io/badge/APM-traces-7c3aed)
![DBM](https://img.shields.io/badge/DBM-banco-b45309)
![SIEM](https://img.shields.io/badge/SIEM-eventos-be123c)
![RCA](https://img.shields.io/badge/RCA-casos-44403c)
![LDAP](https://img.shields.io/badge/Auth-LDAP-1d4ed8)
![Windows](https://img.shields.io/badge/Token-DPAPI-0078D4?logo=windows&logoColor=white)

Servidor [MCP](https://modelcontextprotocol.io) em .NET para consultar uma instância do GuardWatch. A autenticação usa o LDAP já configurado nessa instância. As ferramentas devolvem o mesmo recorte que a conta enxerga na interface: o RBAC vale também aqui.

A URL e o provedor LDAP ficam na configuração de cada instalação. O pacote não traz endereço, usuário nem senha.

Repositório: https://github.com/SouzaMatheus-dev/guardwatch-mcp

## Escopo

Leitura de logs, métricas, Kubernetes, alertas, saúde, APM, banco (DBM), SIEM e RCA.

A credencial entra por arquivo local ou variável de ambiente. O token da sessão fica cifrado com DPAPI em `%LOCALAPPDATA%\GuardWatch\Mcp\token.bin`. Respostas grandes são cortadas. Campos com senha, token, kubeconfig ou segredo saem como `[redigido]`.

Criar cluster, alterar regra, disparar coleta ou mudar retenção continua na interface do GuardWatch.

## Requisitos

| Item | Detalhe |
| --- | --- |
| Sistema | Windows, para o DPAPI do token |
| .NET | SDK 10 para desenvolver. A ferramenta instalada usa o runtime exigido pelo `dotnet tool` |
| Rede | HTTPS até a origem do GuardWatch |
| Conta | Usuário LDAP com permissão de leitura nessa instância |

## Instalação

```powershell
dotnet tool install --global McpGuardWatch
dotnet tool update --global McpGuardWatch
```

O comando instalado é `mcp-guardwatch`.

No Cursor, com a ferramenta global:

```json
{
  "mcpServers": {
    "guardwatch": {
      "command": "mcp-guardwatch"
    }
  }
}
```

Ou via `dnx`, sem instalação prévia:

```json
{
  "mcpServers": {
    "guardwatch": {
      "command": "dnx",
      "args": ["McpGuardWatch", "--yes"]
    }
  }
}
```

A ferramenta instalada não enxerga o `guardwatch.local.json` desta pasta. Grave `%USERPROFILE%\.guardwatch\mcp.json` ou aponte `GUARDWATCH_CONFIG`.

## Configuração

A senha não entra no repositório. Copie o exemplo e preencha o usuário de rede:

```powershell
Copy-Item guardwatch.local.example.json guardwatch.local.json
```

```json
{
  "baseUrl": "https://guardwatch.example",
  "ldapProviderId": 1,
  "username": "seu.usuario",
  "password": "sua-senha-ldap"
}
```

`baseUrl` é a origem do GuardWatch, sem caminho. `ldapProviderId` é o `id` devolvido por `GET /api/v1/auth/providers` nessa mesma origem. O valor `1` do exemplo é só um marcador.

O processo procura a configuração nesta ordem. Variável de ambiente ganha do arquivo.

1. `GUARDWATCH_CONFIG`, se o caminho existir.
2. `guardwatch.local.json` no diretório atual.
3. `guardwatch.local.json` ao lado do executável.
4. `%USERPROFILE%\.guardwatch\mcp.json`.

| Variável | Função |
|---|---|
| `GUARDWATCH_BASE_URL` | Origem do GuardWatch. Obrigatória se o arquivo não trouxer `baseUrl`. Um caminho na URL é ignorado. |
| `GUARDWATCH_LDAP_PROVIDER_ID` | Id do provedor LDAP. Obrigatório no login por usuário e senha. |
| `GUARDWATCH_LDAP_USERNAME` | Usuário de rede. |
| `GUARDWATCH_LDAP_PASSWORD` | Senha. Não é gravada de novo em disco. |
| `GUARDWATCH_TOKEN` | Bearer já emitido. Se expirar e houver usuário LDAP, o processo entra de novo. |

Quem instala a ferramenta fora desta pasta deve usar `%USERPROFILE%\.guardwatch\mcp.json` ou `GUARDWATCH_CONFIG`, porque o diretório atual do Cursor pode não ser o do projeto.

## Uso a partir deste repositório

Crie `.cursor/mcp.json` nesta pasta para subir o servidor com `dotnet run`. Esse arquivo fica fora do repositório. Abra a pasta, habilite o servidor `guardwatch` e faça a primeira pergunta. O login ocorre na primeira ferramenta.

```json
{
  "mcpServers": {
    "guardwatch": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/GuardWatch.Mcp/GuardWatch.Mcp.csproj",
        "--no-launch-profile"
      ]
    }
  }
}
```

O prompt `investigar_servico` pede o alvo e o ambiente: PRD, HML ou DEV. Os três convivem na mesma instância, então a análise espera essa escolha antes de consultar. No chat, o servidor também pergunta o ambiente antes da primeira leitura.

## Catálogo

Todas as ferramentas são leitura. Janelas têm teto para não puxar histórico demais. Listas voltam resumidas.

### Identidade e logs

| Ferramenta | Uso |
|---|---|
| `whoami` | Usuário, papel e permissões da sessão. |
| `query_logs` | Busca na tela de Logs. Severidade, serviço, ambiente e tipo de erro entram no texto da consulta, porque a API v2 ignora esses campos soltos. |
| `log_histogram` | Volume no tempo. |
| `log_patterns` | Mensagens que mais se repetem. |
| `log_facets` | Contagem por serviço, severidade, namespace e cluster. |
| `log_stats` | Volume ingerido na plataforma. |
| `log_index_health` | Atraso da ingestão. |
| `list_log_services` | Serviços que enviam log. |
| `log_services_by_machine` | Os mesmos serviços, pela máquina. |
| `log_service_aliases` | Apelidos usados na busca. |
| `log_filter_options` | Valores aceitos nos filtros. |
| `log_saved_filters` | Filtros salvos na conta. |
| `logs_by_trace` | Logs de um trace id. |
| `log_context` | Linhas ao redor de um evento. |

### Máquinas, containers e alertas

| Ferramenta | Uso |
|---|---|
| `list_machines` | Máquinas monitoradas. O id alimenta as métricas. |
| `machine_detail` | Cadastro de uma máquina. |
| `dashboard_summary` | Resumo do dashboard, geral ou de uma máquina. |
| `machine_metrics` | Amostras recentes de CPU, memória, disco e rede. |
| `machine_metrics_history` | Histórico. Intervalo `hourly`, `minute` ou `raw`. |
| `machine_uptime` | Disponibilidade. |
| `machine_processes` | Processos. |
| `machine_services` | Serviços do sistema. |
| `machine_containers` | Containers vistos na máquina. |
| `list_containers` | Containers, com filtro de host ou Swarm. |
| `container_metrics` | CPU e memória de um container. |
| `container_logs` | Logs ingeridos do container. |
| `alert_summary` | Resumo de alertas por horas. |
| `list_alerts` | Alertas visíveis. |
| `alert_analytics` | Analítica por dias. |
| `list_alert_rules` | Regras. |
| `alert_rule_metrics` | Métricas de domínio das regras. |
| `list_escalation_policies` | Políticas de escalonamento. |

### Kubernetes

O id do cluster sai de `list_k8s_clusters`.

| Ferramenta | Uso |
|---|---|
| `list_k8s_clusters` | Clusters e o status visto pelo GuardWatch. |
| `k8s_snapshot` | Fotografia reduzida do cluster. |
| `list_k8s_namespaces` | Namespaces. |
| `list_k8s_nodes` | Nós. |
| `list_k8s_pods` | Pods. Informe o namespace em cluster grande. |
| `list_k8s_deployments` | Deployments e réplicas. |
| `list_k8s_services` | Services. |
| `list_k8s_ingresses` | Ingresses. |
| `list_k8s_hpas` | Autoscalers. |
| `k8s_events` | Eventos. Por padrão esconde `Normal`. |
| `k8s_pod_logs` | Cauda do log do pod. Histórico fica em `query_logs`. |
| `k8s_pod_usage` | CPU e memória do pod. |
| `k8s_node_usage` | CPU e memória do nó. |
| `k8s_pod_yaml` | YAML do pod. |
| `k8s_resource_yaml` | YAML de deployment, service, ingress e outros tipos. |

### Saúde, APM, banco, SIEM e RCA

| Ferramenta | Uso |
|---|---|
| `system_health` | Componentes internos do GuardWatch. |
| `list_health_checks` | Checks configurados. |
| `health_check_results` | Resultados recentes. |
| `health_check_uptime` | Uptime de um check. |
| `apm_overview` | Serviços mais lentos e com erro. |
| `list_apm_services` | Serviços com telemetria. |
| `apm_traces` | Traces. `has_error` restringe a falhas. |
| `apm_trace` | Spans de um trace. |
| `apm_stats` | Latência e erro. |
| `apm_error_groups` | Grupos de erro. |
| `apm_runtime_metrics` | Heap, threads e runtime. |
| `apm_service_map` | Dependências entre serviços. |
| `dbm_overview` | Visão do banco. |
| `list_dbm_connections` | Conexões monitoradas. |
| `dbm_connection` | Detalhe de uma conexão. |
| `dbm_query_metrics` | Consultas mais caras. |
| `dbm_query_metrics_aggregated` | Consultas agregadas. |
| `dbm_host_metrics` | Host do banco. |
| `dbm_active_sessions` | Sessões ativas. |
| `dbm_database_stats` | Estatística por database. |
| `dbm_table_stats` | Tabelas. |
| `dbm_lock_events` | Locks. |
| `siem_overview` | Resumo do SIEM. |
| `siem_events` | Eventos recentes. |
| `siem_event` | Um evento. |
| `siem_incidents` | Incidentes. |
| `siem_incident_logs` | Logs de um incidente. |
| `siem_rules` | Regras de detecção. |
| `siem_event_trend` | Tendência. |
| `siem_response_metrics` | Resposta a incidente. |
| `rca_overview` | Resumo de causa raiz. |
| `rca_cases` | Casos. |
| `rca_case` | Um caso. |
| `rca_case_events` | Linha do tempo do caso. |

Se a conta não tiver a feature, a ferramenta devolve o erro do GuardWatch. No perfil reader, SIEM, RCA e observabilidade de LLM podem estar desligados mesmo com a rota existindo.

## Desenvolvimento

```powershell
dotnet test GuardWatch.slnx
dotnet run --project src/GuardWatch.Mcp/GuardWatch.Mcp.csproj --no-launch-profile -- --check
```

`--check` autentica e lê o usuário, o volume de logs, os clusters e uma amostra de erro da última hora. A senha não entra na saída.

`guardwatch.local.json` está no `.gitignore`. Confira o `git status` antes de commitar.

A publicação no NuGet dispara na tag `v*`. O workflow `.github/workflows/publish-nuget.yml` testa, empacota e envia com Trusted Publishing, na conta `matneves`.

## Limitações

- A busca de logs por severidade, serviço, ambiente e tipo de erro é montada no texto (`severity:error`), no formato que a API v2 aceita.
- Histograma, facetas e padrões recebem esse mesmo texto e também os filtros estruturados. Se a API ignorar o texto numa dessas rotas, filtre por `service`, `namespace` ou `cluster_name`.
- Listas e séries são cortadas. O campo `*_total`, quando existe, diz quantos itens a API mandou.
- YAML de pod pode ainda conter dado operacional. Chaves de senha e token são redigidas linha a linha.
- A conexão TLS às vezes cai na primeira tentativa. O cliente repete a chamada algumas vezes antes de falhar.
- Este pacote não cobre escrita: criar cluster, alterar regra, disparar coleta de banco ou mudar retenção continuam só na interface do GuardWatch.

## Solução de problemas

| Sintoma | O que fazer |
|---|---|
| `Credenciais ausentes` | Crie `guardwatch.local.json` ou defina as variáveis LDAP. |
| `Falha no login LDAP` | Confira usuário, senha e se o `ldapProviderId` segue ativo em `/api/v1/auth/providers`. |
| HTTP 401 em seguida | Apague `%LOCALAPPDATA%\GuardWatch\Mcp\token.bin` e deixe o processo entrar de novo. |
| HTTP 403 ou feature desligada | A conta não tem essa leitura. `whoami` mostra as permissões. |
| `The SSL connection could not be established` | Repita. Se persistir, a inspeção TLS da rede bloqueou o .NET. O navegador e o SDK usam depósitos de certificado diferentes. |
| A ferramenta NuGet não acha a senha | Grave `%USERPROFILE%\.guardwatch\mcp.json`. O `dotnet run` desta pasta acha o `guardwatch.local.json` local. A ferramenta instalada não. |

## Licença

[MIT](LICENSE).
