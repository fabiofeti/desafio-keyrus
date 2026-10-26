# Software — estratégia de testes e aceite

## Testes unitários
- Débito/crédito e valor positivo; saldo correto e precisão decimal.
- Data de negócio; entradas inválidas.
- Idempotency key repetida e payload divergente.

## Testes de integração
- EF Core + PostgreSQL real em container.
- POST grava lançamento e Outbox atomicamente.
- Publisher entrega evento e marca Outbox apenas após confirmação.
- Consumer duplica evento sem duplicar saldo (Inbox).
- Role Transactions não pode INSERT/UPDATE em `consolidation`; role Consolidation não pode gravar em `transactions`.

## Resiliência / caos local
1. Subir Compose e registrar baseline.
2. Parar Worker; executar POST de lançamentos e confirmar sucesso e Outbox pendente/fila acumulada.
3. Reiniciar Worker; aguardar convergência e conferir soma exata.
4. Interromper RabbitMQ, continuar POST, restaurar broker e verificar publicação posterior.
5. Redeliver mesmo eventId e confirmar saldo inalterado.

## Carga: 50 RPS
Testar `GET /daily-balances/{date}` por janela representativa (por exemplo 2 minutos), com gerador k6 ou equivalente. Registrar ambiente, script, total esperado/enviado, respostas 2xx, 4xx, 5xx, timeouts, RPS efetivo, p95/p99 e duração.

**Critério proposto de perda HTTP:** `(requisições sem resposta válida / requisições enviadas) * 100 <= 5%`; separar erros funcionais 4xx de falhas técnicas e definir a interpretação antes de executar. O enunciado não fixa percentil de latência; usar metas internas identificadas como propostas.

## Evidência
Preencher após rodar:
| Cenário | Comando/script | Resultado | Evidência |
|---|---|---|---|
| Unitários | Pendente | Não executado | — |
| Integração | Pendente | Não executado | — |
| Queda Worker | Pendente | Não executado | — |
| Queda broker | Pendente | Não executado | — |
| Carga 50 RPS | Pendente | Não executado | — |

Não marcar sucesso sem execução.
