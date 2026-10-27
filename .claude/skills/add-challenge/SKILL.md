---
name: add-challenge
description: 'Leva um item do desafio Keyrus (P0 ou P1 de docs/IMPLEMENTATION-PLAN.md) do pendente ao concluído com evidência - escolhe o próximo item na ordem de prioridade, delega a construção aos skills certos, passa pelos quatro gates (build/arquitetura, testes, evidência executada, documentação) e só então marca o checkbox. Use quando o usuário pedir para implementar o próximo item, um requisito, uma etapa do plano, o teste de falha, o teste de carga de 50 RPS, ou para atualizar evidências.'
argument-hint: '<item do plano, ex.: "P0 GET /daily-balances" ou "próximo">'
---

# Implementar um item do desafio

No CoreMastery, um *challenge* é dado que passa por quatro gates antes de ser
publicado. Aqui, o desafio é o próprio enunciado da Keyrus, e cada item do
[plano](../../../docs/IMPLEMENTATION-PLAN.md) passa por quatro gates antes de
ganhar `[x]`. Um item marcado sem evidência é a única forma de reprovar esta
prova com tudo compilando.

## Onde um item vive

`docs/IMPLEMENTATION-PLAN.md`, seções **P0** (indispensável), **P1** (se houver
tempo) e **P2** (só documentar — nunca implementar sem pedido explícito). A
evidência vai para a tabela de [docs/software/03-testing.md](../../../docs/software/03-testing.md#evidência)
e, para execução local, para o `README.md`.

## Fluxo

1. **Escolha o item.** Sem argumento ou com "próximo": o primeiro `- [ ]` de P0,
   na ordem do arquivo. P1 só depois de todo P0 marcado. Respeite também a ordem
   de etapas do `CLAUDE.md`: solução/compose → Transactions → Outbox/RabbitMQ →
   Consolidation/Inbox → leitura → testes → carga/falha → documentação.
   Caminho feliz end-to-end primeiro, resiliência depois.
2. **Diga o que vai fazer** em uma ou duas linhas: o item, os arquivos que vai
   tocar, como vai provar. Se o item for ambíguo, decida pela leitura mais simples
   que satisfaz o enunciado e registre a decisão.
3. **Construa com o skill certo**:

   | Item | Skill |
   |---|---|
   | Solução, Dockerfiles, Compose, schemas/roles | `scaffold-solution`, `add-module` |
   | Tabela/agregado, migration | `add-entity` |
   | Endpoint (POST/GET) | `add-feature` |
   | Outbox, publisher, consumer, Inbox | `add-integration-event` |
   | Testes faltando | `add-feature` → [tests](../add-feature/references/tests.md) |
   | Falha e carga | este skill, seções abaixo |

4. **Passe pelos quatro gates**, em ordem. Um gate vermelho volta ao passo 3.
5. **Marque o checkbox** só depois do gate 4, e informe ao usuário o que foi
   executado e o resultado real.

## Os quatro gates

| Gate | Comando | Pega |
|---|---|---|
| 1. Build + arquitetura | `dotnet build CashFlow.slnx` e `dotnet test tests/CashFlow.ArchitectureTests` | Referência entre módulos, camada errada, módulo não registrado |
| 2. Testes | `dotnet test CashFlow.slnx` (inclui integração; Docker ligado) | Regra, idempotência, Outbox/Inbox, isolamento por role |
| 3. Evidência executada | O comando do item rodado de verdade: `docker compose up --build -d`, `curl`, cenário de falha, k6 | O que só aparece com processo, broker e banco reais |
| 4. Documentação | Plano, tabela de evidência, README, diagrama se mudou | Doc dizendo uma coisa e o código fazendo outra (regra 9) |

Gate 3 não se aplica a item puramente de código coberto por teste (ex.: "unitários
para regras") — diga isso explicitamente em vez de pular em silêncio.

## Cenário de falha (P0: parar Worker e convergir)

Roteiro de [03-testing](../../../docs/software/03-testing.md#resiliência--caos-local),
executado e registrado. Script sugerido: `tests/chaos/worker-down.sh`.

1. `docker compose up --build -d` e aguardar `api` healthy.
2. Baseline: `GET /daily-balances/{data}` da data de teste.
3. `docker compose stop worker`.
4. N POSTs (ex.: 100), com keys únicas, valores conhecidos. Todos `201`.
   Conferir Outbox pendente: `SELECT count(*) FROM transactions.outbox_messages WHERE published_on_utc IS NULL`
   (via `docker compose exec postgres psql …`).
5. `docker compose start worker`; aguardar até o saldo parar de mudar (polling com timeout).
6. Conferir **soma exata**: saldo final − baseline = soma calculada pelo script.
   Outbox pendente = 0. Linhas na Inbox = N.
7. Redelivery: republicar manualmente um eventId já processado (management API do
   RabbitMQ ou teste de integração) e confirmar saldo inalterado.

Variação P0/P1 do broker: `docker compose stop rabbitmq`, POSTs continuam `201`,
Outbox acumula; `start rabbitmq`, Outbox drena, saldo converge.

## Teste de carga (P0: 50 RPS no consolidado)

Defina o critério **antes** de rodar, como manda o doc de testes:
perda = (requisições sem resposta válida ÷ enviadas) × 100 ≤ 5%, onde
"resposta válida" = HTTP 200 com corpo JSON do saldo dentro do timeout; 4xx
funcional conta à parte; 5xx, timeout e erro de conexão contam como perda.

`tests/load/daily-balances.js` (k6, executor `constant-arrival-rate`):

```javascript
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    daily_balances: {
      executor: 'constant-arrival-rate',
      rate: 50, timeUnit: '1s', duration: '2m',
      preAllocatedVUs: 50, maxVUs: 200,
    },
  },
  thresholds: {
    // Critério do enunciado: no máximo 5% de perda.
    'checks{check:valid_response}': ['rate>=0.95'],
    // Metas internas propostas, não do enunciado.
    http_req_duration: ['p(95)<300', 'p(99)<800'],
  },
};

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const DATE = __ENV.DATE || '2026-10-26';

export default function () {
  const res = http.get(`${BASE}/daily-balances/${DATE}`, { timeout: '2s' });
  check(res, { valid_response: (r) => r.status === 200 && r.json('date') === DATE }, { check: 'valid_response' });
}
```

k6 não está instalado localmente; rode pela imagem, na rede do compose:

```bash
docker run --rm -i --network cashflow_default \
  -e BASE_URL=http://api:8080 -e DATE=2026-10-26 \
  -v "$PWD/tests/load:/scripts" grafana/k6 run --summary-export=/scripts/results/daily-balances.json /scripts/daily-balances.js
```

Registre na tabela de evidência: ambiente (máquina, WSL2, recursos do Docker),
script, duração, total esperado (6000) e enviado, 2xx/4xx/5xx, timeouts, RPS
efetivo, p95/p99, taxa de perda, e se os thresholds passaram. **Se falhar,
registre a falha** com os números — não ajuste threshold para passar, e não
escreva "atende 50 RPS" sem esse registro (regra 7).

Rode também com o Worker parado, para mostrar que a leitura do consolidado não
depende do processamento estar vivo.

## Nunca

- Marcar `[x]` sem gate 2 verde e, quando aplicável, gate 3 executado.
- Implementar P2 ou adicionar funcionalidade que o enunciado não pede.
- Escrever número de desempenho que não saiu de uma execução.
- Mudar decisão do ADR-001 sem dizer ao usuário e registrar no ADR.
