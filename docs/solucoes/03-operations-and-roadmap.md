# Soluções — operação, custos, riscos e evolução

## Operação e observabilidade
Healthchecks para API, PostgreSQL e RabbitMQ; readiness distinta de liveness. Logs estruturados com `correlationId`, `transactionId`, `eventId`; métricas: RPS, p95/p99, taxa de erro, profundidade da fila, idade da Outbox, lag da consolidação, retries e falhas do consumidor.

## Riscos e mitigação
| Risco | Mitigação |
|---|---|
| Worker parado | Eventos persistidos, fila durável, reprocessamento |
| RabbitMQ indisponível | Outbox persiste no banco; publisher retenta com limites |
| Mensagem duplicada | Inbox/constraint única e transação atômica |
| Banco indisponível | Limitação do MVP; backup/HA em produção |
| Crescimento da fila | Monitorar lag, escalar consumidores, avaliar backpressure |
| Dados temporariamente desatualizados | Informar consistência eventual no contrato de leitura |

## Custos
Ambiente local: imagens gratuitas e sem licenças comerciais obrigatórias; custo depende de recursos do host. Produção: estimar separadamente computação da API/Worker, PostgreSQL gerenciado, RabbitMQ gerenciado/VM, observabilidade, rede e backups **após** escolher cloud, região, SLA e volumetria. Não apresentar números sem cotação.

## Arquitetura de transição
Não se aplica: o enunciado não informa legado. Se existisse, sugerir strangler e migração gradual de lançamentos, mas não incluir implementação fictícia.

## Evoluções futuras
Autenticação/autorização, DLQ e replay, dashboards, escalabilidade independente, HA/DR, alertas, trilhas de auditoria e isolamento físico de bancos se necessário.
