# Soluções — operação, custos, riscos e evolução

## Operação e observabilidade
Healthchecks com readiness distinta de liveness: no MVP, `/health/ready` da API testa o PostgreSQL de cada módulo com a própria role, e o RabbitMQ fica fora porque a API não depende dele ([implementado](../software/02-quality-attributes.md#observabilidade-implementado)). Logs estruturados com `correlationId`, `transactionId`, `eventId`; métricas: RPS, p95/p99, taxa de erro, profundidade da fila, idade da Outbox, lag da consolidação, retries e falhas do consumidor.

## Riscos e mitigação
| Risco | Mitigação |
|---|---|
| Worker parado | Eventos persistidos, fila durável, reprocessamento |
| RabbitMQ indisponível | Outbox persiste no banco; publisher retenta com limites |
| Mensagem duplicada | Inbox/constraint única e transação atômica |
| Mensagem que sempre falha (poison) | Fila quorum com limite de entregas; após o limite vai para `consolidation.transaction-created.v1.dlq` e a fila segue; monitorar profundidade da DLQ e reprocessar manualmente |
| Banco indisponível | Limitação do MVP; backup/HA em produção |
| Crescimento da fila | Monitorar lag, escalar consumidores (3 réplicas do Worker medidas sem duplicar efeito), avaliar backpressure |
| Dados temporariamente desatualizados | Informar consistência eventual no contrato de leitura |

## Custos
Ambiente local: imagens gratuitas e sem licenças comerciais obrigatórias; custo depende de recursos do host. Produção: estimar separadamente computação da API/Worker, PostgreSQL gerenciado, RabbitMQ gerenciado/VM, observabilidade, rede e backups **após** escolher cloud, região, SLA e volumetria. Não apresentar números sem cotação.

## Arquitetura de transição
Não se aplica: o enunciado não informa legado. Se existisse, sugerir strangler e migração gradual de lançamentos, mas não incluir implementação fictícia.

## Evoluções futuras
Autenticação/autorização, replay automatizado da DLQ, dashboards, escalabilidade independente, HA/DR, alertas, trilhas de auditoria e isolamento físico de bancos se necessário. Proposta por item, o que já existe no código, e o procedimento para extrair um módulo para microsserviço: [04-evolution-and-module-extraction](04-evolution-and-module-extraction.md).
