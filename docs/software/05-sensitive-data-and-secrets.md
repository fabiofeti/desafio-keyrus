# Software — dados sensíveis e gestão de segredos

Como o projeto trata senhas, connection strings, endereços de banco e fila, e o que muda para produção no Azure. A primeira parte é **auditoria executada**; o resto é prática e proposta.

## Resultado da auditoria (2026-10-26)
| Verificação | Como | Resultado |
|---|---|---|
| Segredos em todo o histórico do Git | `gitleaks` v8.30.1 (`docker run zricethezav/gitleaks git /repo --redact`) | 24 commits, ~633 KB: **nenhum vazamento** |
| `appsettings*.json` | Leitura dos dois arquivos | Só níveis de log e, no Worker, `RabbitMq:Host`, `Port`, `PrefetchCount`, `DeliveryLimit`. Nenhuma senha nem connection string |
| Padrões em código | `RabbitMqOptions` | Usuário e senha vazios por padrão (sem `guest`); a validação de opções derruba o Worker na partida se faltarem |
| Connection strings | Busca em `src` e no Compose | Nenhuma em código; no Compose são montadas a partir de variáveis (`${TRANSACTIONS_APP_PASSWORD}` etc.), e o Compose recusa subir se uma faltar (`:?`) |
| Dockerfiles e imagem | Leitura; `.dockerignore` | Sem `ARG`/`ENV` de segredo; o `.env` está no `.dockerignore`, então nem entra no contexto de build |
| Logs em runtime | 4.408 linhas dos containers da stack | Nenhuma senha: nem a chave de senha de uma connection string, nem URL AMQP com credencial embutida. A conexão ao broker loga só host e porta |
| Respostas de erro | `UnhandledExceptionHandler`, `ExceptionHandlingDecorator` | O cliente recebe `Error.Unhandled` genérico; a exceção fica só no log |
| Payload em log | `LoggingDecorator` | Registra nome do command e código do erro, nunca o conteúdo |
| EF Core e Npgsql | Busca por `EnableSensitiveDataLogging` | Não usado; o Npgsql remove a senha da connection string depois de abrir a conexão (comportamento padrão, `Persist Security Info` desligado) |
| Senhas literais | Busca no repositório | Só em `tests/CashFlow.IntegrationTests/IntegrationTestWebAppFactory.cs` (valores `*-test-only`), usadas por containers descartáveis do Testcontainers |

## Inventário do que é sensível
| Dado | Por que importa | Quem usa | Onde vive hoje (local) |
|---|---|---|---|
| Senha do administrador do PostgreSQL (`POSTGRES_PASSWORD`) | Dá DDL e acesso aos dois schemas | Só o `migrator` e o script de init | `.env` local, fora do Git |
| Senhas das roles `transactions_app` e `consolidation_app` | Cada uma dá leitura e escrita no próprio schema | `api` e `worker` | `.env` → variáveis do container |
| Usuário e senha do RabbitMQ | Publicar, consumir e administrar as filas | `worker` e a management UI | `.env` → variáveis do container |
| Connection strings | Contêm a senha da role | `api`, `worker`, `migrator` | Montadas no `docker-compose.yml` a partir das variáveis |
| Endereços (host do banco, host, vhost e porta do broker, portas) | Não são segredo, mas ajudam um atacante a mapear a rede | Todos | Configuração (`appsettings`, variáveis), nunca código |
| `description` do lançamento | Texto livre: pode conter dado pessoal | Transactions | Só no schema `transactions`; **não** vai no evento nem no log |
| `X-Correlation-Id` | Vem do cliente e vai para log e banco | API e Worker | Validado (`^[A-Za-z0-9-]{1,100}$`) antes de ser usado; outro valor é trocado por um gerado |

A regra do projeto: **segredo é configuração injetada, nunca código nem arquivo versionado.** O código só conhece o *nome* da configuração (`ConnectionStrings:Transactions`, `RabbitMq:Password`).

## Controles já implementados
- **Fora do Git por construção:** `.env` no `.gitignore` e no `.dockerignore`; só o [.env.example](../../.env.example) é versionado, com `change-me`, e o [README](../../README.md#3-criar-o-env-com-senhas-próprias) gera senhas aleatórias no primeiro passo.
- **Falha cedo:** o Compose não sobe sem as variáveis, e o Worker não sobe sem usuário e senha do broker. Nenhum valor padrão "funciona por acidente" em produção.
- **Menor privilégio no banco:** uma role por módulo, sem `DELETE`, sem DDL, sem acesso ao schema do outro ([01-roles.sh](../../deploy/postgres/init/01-roles.sh)). A credencial administrativa só existe no `migrator`, que termina depois das migrations. Provado por `DatabaseRoleTests` (erro `42501`).
- **Uma credencial por módulo:** cada `DbContext` lê a própria connection string; hospedar os dois módulos no Worker não dá a nenhum deles acesso ao outro. Isso também mantém o isolamento se um módulo for extraído ([04-evolution](../solucoes/04-evolution-and-module-extraction.md)).
- **Nada sensível nas mensagens:** o `TransactionCreated.v1` leva ids e valores, sem `description`.
- **Proteção no fluxo de desenvolvimento:** o hook [.claude/hooks/guard-secrets.sh](../../.claude/hooks/guard-secrets.sh) bloqueia a gravação de credencial literal em arquivos do projeto (bloqueou até um rascunho deste documento que descrevia o padrão procurado).

## Limitações conscientes do ambiente local
Aceitáveis para rodar o desafio numa máquina de desenvolvimento, **não** para produção:

| Limitação | Risco | Em produção |
|---|---|---|
| Segredos como variáveis de ambiente do container | Quem tem acesso ao Docker lê com `docker inspect` | Key Vault + Managed Identity (abaixo) |
| Sem TLS entre API/Worker e PostgreSQL/RabbitMQ | Tráfego em texto na rede do Compose | TLS com validação de certificado no Npgsql (`SSL Mode` em `VerifyFull`), AMQPS na porta 5671, TLS no Redis |
| PostgreSQL, AMQP e management UI publicados no host | Acessíveis de fora da máquina se o firewall permitir | Endpoints privados; management só por VPN ou bastion. Localmente dá para restringir prefixando `127.0.0.1:` na porta publicada do Compose |
| `.env` em texto no disco | Quem lê o disco lê as senhas | Não existe `.env` em produção |
| OpenAPI servido em qualquer ambiente | Expõe o contrato | Restringir por ambiente ou autenticação |

## Boas práticas para produção (Azure)
### 1. Identidade antes de segredo
O melhor segredo é o que não existe. Com **Managed Identity**, o serviço se autentica no recurso sem senha armazenada:

| Recurso | Sem senha | Como |
|---|---|---|
| Azure Database for PostgreSQL | Sim, autenticação Entra ID | Um token de acesso faz o papel da senha; no Npgsql, um provedor de senha periódico (`UsePeriodicPasswordProvider`) obtém o token com `DefaultAzureCredential`. Uma identidade por módulo, mapeada para a role do módulo |
| Azure Managed Redis / Azure Cache for Redis | Sim, autenticação Entra ID | `Microsoft.Azure.StackExchangeRedis` com a identidade do pod |
| Azure Key Vault | Sim | `DefaultAzureCredential` |
| RabbitMQ | Não nativamente | Usuário e senha no Key Vault (ou o plugin OAuth 2.0 do RabbitMQ com Entra ID). Trocar para Azure Service Bus daria autenticação por identidade, mas é outra decisão de arquitetura |

### 2. Key Vault como fonte única dos segredos que restarem
- **Separar segredo de configuração:** host, porta, vhost, nome do banco e parâmetros ficam em configuração comum (variáveis de ambiente do App Service ou Container Apps, ou ConfigMap); só senha e chave vão para o Key Vault.
- **Como a aplicação lê**, sem mudar o código, porque o projeto já lê tudo pela configuração:
  - **Referência do Key Vault na configuração da plataforma:** referência `@Microsoft.KeyVault(SecretUri=...)` no App Service, *secret* com referência ao Key Vault no Container Apps, ou o Secrets Store CSI Driver no AKS. A aplicação continua lendo `ConnectionStrings__Transactions` ou `RabbitMq__Password`.
  - **Ou pelo provedor de configuração:** `builder.Configuration.AddAzureKeyVault(uri, new DefaultAzureCredential())`. No nome do segredo, `--` faz o papel de `:` (por exemplo, `ConnectionStrings--Transactions`).
- **Acesso por RBAC:** cada identidade só lê os segredos do próprio módulo (`Key Vault Secrets User` com escopo no segredo). O Worker de Consolidation não lê a senha de Transactions.

### 3. Rotação
- **Rotação sem parada com duas credenciais:** cria a nova, publica no Key Vault, as aplicações passam a usá-la, e só então a antiga é revogada. No PostgreSQL vale o mesmo com duas roles de login que herdam os mesmos grants.
- **Recarregar sem reiniciar:** o provedor do Key Vault aceita intervalo de recarga, e quem lê por `IOptionsMonitor` passa a ver o novo valor. Conexões já abertas no pool continuam válidas até fechar.
- **Com Managed Identity não há o que rodar:** os tokens expiram e são renovados sozinhos.

### 4. Transporte e rede
- TLS obrigatório em todas as conexões, com validação de certificado.
- Banco, broker e cache em **endpoints privados**, sem IP público; a API atrás de Front Door ou Application Gateway.
- A management UI do RabbitMQ nunca exposta à internet.

### 5. Higiene de logs e telemetria
- Nunca logar connection string, token, header `Authorization` ou payload completo. Hoje isso já vale: os logs trazem nome do command, código de erro e ids.
- Não usar dado pessoal nem id de usuário como *tag* de métrica (cardinalidade e privacidade); o `oid` do Entra ID vai em log, não em métrica.
- No Application Insights ou no OpenTelemetry, filtrar o texto de SQL e headers sensíveis antes de exportar.

### 6. Repositório e pipeline
- **Varredura de segredos no CI:** `gitleaks` em todo PR, além do *push protection* do GitHub. Este repositório é público, e um segredo commitado precisa ser tratado como vazado, mesmo que o commit seja apagado depois.
- **Nada de segredo em imagem:** sem `ARG`/`ENV` com senha nos Dockerfiles (hoje não há), e o `.env` fora do contexto de build (`.dockerignore`).
- **Segredos do pipeline** em *environments* protegidos do GitHub Actions ou do Azure DevOps, com federação OIDC com o Azure, sem *client secret*.

### 7. Se um segredo vazar
1. **Rodar primeiro:** gerar a credencial nova e revogar a antiga. Reescrever o histórico do Git não basta, porque clones e forks já têm o valor.
2. Depois, limpar o histórico (`git filter-repo` ou BFG) e invalidar caches e artefatos.
3. Revisar os logs de acesso do recurso afetado durante a janela de exposição.

## Checklist antes de publicar um ambiente
- [ ] Nenhum `.env` nem segredo em variável de ambiente em texto puro; segredos por referência ao Key Vault ou por Managed Identity.
- [ ] Uma identidade ou role por módulo, com o menor privilégio (sem `DELETE`/DDL em runtime).
- [ ] TLS com validação de certificado em PostgreSQL, RabbitMQ e Redis.
- [ ] Banco, broker e management sem endereço público.
- [ ] `gitleaks` no CI sem achados; push protection ligado.
- [ ] Rotação documentada e testada uma vez.
- [ ] Logs e telemetria sem connection string, token ou payload.
