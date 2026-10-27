# Software — rate limiting e limite de concorrência (proposta, não implementada)

**Decisão:** não implementar rate limiting agora. Este documento deixa pronto o plano para quando houver motivo e números: limite por IP e por usuário do Azure AD (Microsoft Entra ID), configuração em cache híbrido (Redis + memória do pod) parametrizada por variáveis de ambiente do Azure, e semáforo para limitar as chamadas pesadas.

## Por que não agora
Um limite precisa de um número, e hoje não há base para escolher um:

- **Não sabemos o perfil de consumo.** Quantos clientes, quantas chamadas por cliente, picos por horário. O único número do enunciado é o pico de 50 RPS no consolidado, e a API atende isso com folga: 0% de perda, p99 de 2,5 ms ([carga](03-testing.md#carga-50-rps)).
- **Limite chutado é contraproducente.** Baixo demais, devolve `429` para clientes legítimos e vira incidente. Alto demais, não protege nada, mas dá a falsa impressão de que protege.
- **Não há identidade de usuário.** Os endpoints são públicos no MVP; limitar "por usuário" depende primeiro da autenticação com Entra ID.
- **O que já protege hoje:** validação de entrada e tamanho de campo, idempotência no POST (um cliente que repete não duplica nada), a leitura do saldo é uma busca por chave primária, e o Worker já tem seus próprios limites (prefetch 16 por consumidor, lote de 50 na Outbox, timeout de 5 s por publicação).

**Gatilhos para implementar:** um consumidor fazendo polling agressivo, retentativas em massa de um parceiro, custo de infraestrutura crescendo por uso desigual, ou a exigência de planos de uso por cliente.

**Como chegar ao número:** primeiro medir. Com a autenticação no ar, adicionar um contador de requisições por cliente (`oid`/`azp`, ver abaixo) ao `Meter` existente, observar por algumas semanas e definir o limite a partir do percentil de uso real (por exemplo, 3× o p99 de cada cliente). Ligar o limitador primeiro em **modo observação** (limite alto, só registra quem teria sido barrado) e só depois aplicar.

## Cenário: represar chamadas de consumidores
Exemplo concreto: um ERP de um parceiro consulta `GET /daily-balances/{date}` a cada 100 ms para cada uma de 30 datas, e outro cliente reenvia POSTs em loop quando recebe timeout. Os dois disputam a mesma API com os demais clientes.

### Camadas
| Camada | Chave | Para quê | Onde |
|---|---|---|---|
| Borda | IP do cliente | Barrar abuso e varredura antes de chegar aos pods; custa nada para a aplicação | Azure Front Door com regra de WAF do tipo *rate limit*, agrupada por endereço do cliente |
| Gateway (opcional) | Claim do token | Quotas por cliente ou plano, globais entre réplicas | Azure API Management: `validate-azure-ad-token` + `rate-limit-by-key` e `quota-by-key` |
| Aplicação | Usuário ou app do Entra ID; IP como fallback | Limite fino por endpoint, com resposta no formato da API | Middleware de rate limiting do ASP.NET Core |

A borda não substitui a aplicação, e vice-versa: o IP sozinho falha com NAT corporativo (muitos usuários atrás de um IP) e com clientes que trocam de IP; a identidade sozinha não protege endpoints anônimos nem tráfego antes da autenticação.

### Chave por usuário no Entra ID
| Tipo de chamador | Claim | Observação |
|---|---|---|
| Usuário | `oid` (object id) + `tid` (tenant) | `oid` é estável por usuário dentro do tenant; não usar `name` nem `email`, que mudam |
| Aplicação (client credentials) | `azp` (tokens v2) ou `appid` (v1) + `tid` | O consumidor "ERP do parceiro" é uma app; o limite é dela, não de um usuário |
| Sem token | IP do cliente | Só nos endpoints que continuarem anônimos (por exemplo, health) |

Com `Microsoft.Identity.Web`, o `oid` chega como `http://schemas.microsoft.com/identity/claims/objectidentifier`, a menos que o mapeamento de claims esteja desligado; `User.GetObjectId()` resolve os dois casos.

O IP só é confiável atrás de um proxy conhecido: configurar `ForwardedHeaders` com as redes do Front Door/Application Gateway em `KnownIPNetworks`. Sem isso, qualquer cliente forja `X-Forwarded-For` e escapa do limite.

### Políticas propostas (os números são placeholders até a medição)
| Política | Endpoints | Algoritmo | Por quê |
|---|---|---|---|
| `writes` | `POST /transactions` | Token bucket por chave (ex.: 20 tokens, repõe 10/s) | Permite rajada curta de um caixa e segura retentativa em loop |
| `reads` | `GET /transactions`, `GET /daily-balances/{date}` | Janela deslizante por chave (ex.: 300/min, 6 segmentos) | Polling constante é o padrão de abuso típico de leitura |
| `global` | todos | Limite encadeado por pod | Teto de proteção do processo, independente de quem chama |

### Encaixe no código
- **Onde:** `Common.Presentation/RateLimiting/` com as políticas e o `OnRejected`; o host chama `AddRateLimiter` e `app.UseRateLimiter()` depois da autenticação; cada `IEndpoint` declara a sua com `.RequireRateLimiting(RateLimitPolicies.Reads)`, junto dos `Produces` que ele já declara.
- **Partição:** `PartitionedRateLimiter` com a chave `tid:oid`, `tid:azp` ou `ip:<endereço>`, nessa ordem de preferência.
- **Resposta:** `429` em problem details com `code: "RateLimit.Exceeded"` e header `Retry-After`, no mesmo formato que `ApiResults.Problem` já produz; documentado no OpenAPI com `.ProducesProblem(429)` e `.ProducesResponseHeader(429, "Retry-After", ...)`, e o snapshot de contrato acusa a mudança.
- **Observabilidade:** contador `cashflow.ratelimit.rejected` com tags `policy` e tipo de chave (nunca o `oid` como tag, por cardinalidade e privacidade); log de rejeição com o `CorrelationId` que o middleware já coloca no escopo.
- **Testes:** integração por HTTP para `429` + `Retry-After` por política, partição por usuário (dois tokens não dividem o mesmo balde) e fallback por IP.

### Contadores locais e réplicas
O limitador do ASP.NET Core guarda os contadores **na memória de cada pod**. Com N réplicas, o limite efetivo é aproximadamente N × o configurado, e varia com o balanceamento. Três saídas, em ordem de preferência:

1. **Limite global no gateway ou na borda** (APIM ou Front Door), e o da aplicação como proteção local por pod. É o mais simples e não adiciona latência ao request.
2. **Dividir o limite pelo número de réplicas** quando o autoscaling for previsível.
3. **Contadores distribuídos no Redis**, só se for preciso exatidão global na aplicação: custa uma ida ao Redis por request e cria uma dependência no caminho crítico (se o Redis cair, decidir entre liberar ou bloquear).

## Configuração em cache híbrido
O que vai para o cache é a **configuração** dos limites (qual política, qual limite por cliente ou plano), não os contadores. Ela muda pouco e é lida em todo request, por isso precisa estar na memória do pod.

```text
request ─► L1: memória do pod (HybridCache)         ~nanossegundos, TTL curto
             │ miss
             ▼
           L2: Redis (Azure Managed Redis)           ~1 ms, TTL maior, compartilhado
             │ miss
             ▼
           Origem: Azure App Configuration ou tabela de planos por cliente
```

- **Biblioteca:** `Microsoft.Extensions.Caching.Hybrid` (`HybridCache`), com o Redis registrado como cache distribuído (`AddStackExchangeRedisCache`). `GetOrCreateAsync` evita *stampede*: se mil requests pedem a mesma chave fria, só um vai à origem.
- **TTL:** L1 curto (30–60 s) e L2 maior (10 min). O L1 curto limita quanto tempo uma mudança de limite leva para chegar a todos os pods; o `HybridCache` não propaga invalidação entre pods. Se for preciso propagar na hora, usar pub/sub do Redis ou uma biblioteca com *backplane* (por exemplo, FusionCache).
- **Falha do Redis:** o L1 continua servindo o último valor; sem ele, aplicar os **limites padrão** das variáveis de ambiente. A configuração de rate limit nunca pode derrubar a API.
- **Variáveis de ambiente do Azure** (App Service *Application settings*, Container Apps *environment variables*, ou ConfigMap no AKS), no padrão `Seção__Chave` que o .NET já lê, como o resto do projeto:

```text
RateLimiting__Enabled=true
RateLimiting__Mode=Observe                      # Observe | Enforce
RateLimiting__Policies__Reads__PermitLimit=300
RateLimiting__Policies__Reads__WindowSeconds=60
RateLimiting__Policies__Writes__TokenLimit=20
RateLimiting__Policies__Writes__TokensPerSecond=10
Caching__Redis__Endpoint=cashflow.redis.cache.windows.net:10000
Caching__LocalExpirationSeconds=45
Caching__DistributedExpirationSeconds=600
```

- **Segredo fora da variável:** a conexão com o Redis usa Managed Identity com autenticação Entra ID (sem senha); se precisar de chave, ela fica no Key Vault e entra por referência (`@Microsoft.KeyVault(SecretUri=...)` no App Service, *secret* no Container Apps), nunca em texto na configuração — a mesma regra que o projeto já segue com o `.env`.

## Semáforo: limitar as chamadas pesadas
Rate limit controla **quantas chamadas por tempo** um cliente faz. Semáforo controla **quantas chamadas pesadas rodam ao mesmo tempo** no pod, venham de quem vierem. É o que evita que poucas consultas caras esgotem o pool de conexões do PostgreSQL e derrubem também o `POST /transactions` (o padrão *bulkhead*).

Hoje não há consulta pesada (a leitura de saldo é por chave primária). O cenário são evoluções prováveis: extrato por período, exportação de lançamentos, relatório mensal agregado.

### Desenho
- **Marcação no caso de uso:** a query pesada implementa um marcador, por exemplo `IHeavyQuery` com o nome do grupo (`"reports"`).
- **Decorator:** um `ConcurrencyLimitDecorator.QueryHandler<,>` entra no pipeline do Scrutor, junto dos decorators de exceção, log e validação que já existem. Ele só atua nas queries marcadas.
- **Um semáforo por grupo e por pod**, singleton: `SemaphoreSlim(permits)`. Grupos separados para que exportações não esperem por relatórios e nenhum dos dois toque no caminho do POST.
- **Espera com prazo:** `await semaphore.WaitAsync(timeout, cancellationToken)`; se o prazo passar, devolve `Result` com `Error` de código `"Server.Busy"`. Hoje o `ErrorType` não tem um tipo que vire `503`: entra um novo (por exemplo `Unavailable`), mapeado no `ApiResults` para `503` com `Retry-After`, num lugar só, como os demais. Nunca fila infinita: a fila sem limite só adia a queda.
- **Liberação garantida:** `Release()` no `finally`, e o cancelamento do request (`RequestAborted`) também sai da fila.
- **Dimensionamento:** a soma dos permits de todos os grupos fica bem abaixo do `Max Pool Size` do Npgsql (padrão 100) dividido pelas réplicas que compartilham o banco, deixando folga para o POST e para o Worker.
- **Métricas:** histograma do tempo de espera, contador de `Server.Busy`, e um gauge de permits em uso por grupo.

Para limitar no nível do endpoint inteiro, o próprio middleware do ASP.NET Core tem a política de concorrência (`AddConcurrencyLimiter`, com `PermitLimit` e `QueueLimit`), que é um semáforo com fila limitada. O decorator é preferível quando o limite pertence ao caso de uso e não à rota. Para chamadas de saída, como a uma API externa, a mesma ideia já existe no Polly (`AddConcurrencyLimiter`), ao lado do retry e do circuit breaker que o projeto usa no broker.

Entre réplicas, o limite é por pod. Um semáforo distribuído (lock no Redis) só se justifica se o recurso protegido for global e frágil (por exemplo, uma API de terceiro com limite de conexões), porque adiciona latência e um ponto de falha.

## Ordem de implementação sugerida
1. Autenticação Entra ID (pré-requisito do limite por usuário).
2. Métrica de requisições por cliente e semanas de observação.
3. Limite por IP na borda (Front Door/WAF), que não depende de código.
4. Políticas na aplicação em modo `Observe`, com a configuração no cache híbrido.
5. `Enforce`, com os números da medição e os testes de `429`.
6. Semáforo por grupo quando a primeira consulta pesada entrar no produto.
