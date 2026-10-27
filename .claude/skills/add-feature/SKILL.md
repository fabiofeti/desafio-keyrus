---
name: add-feature
description: 'Cria uma fatia vertical completa num módulo do CashFlow - command ou query, handler, validator FluentValidation, endpoint Minimal API como IEndpoint na Presentation do módulo, e testes unitários, de validator e de integração. Use quando o usuário pedir para adicionar uma feature, caso de uso, command, query ou endpoint (ex.: POST /transactions, GET /transactions, GET /daily-balances/{date}).'
argument-hint: '<descrição da feature, ex.: "criar lançamento" ou "consultar saldo diário">'
---

# Adicionar uma feature (fatia vertical)

Um caso de uso, uma pasta, tudo o que ele precisa. Sem MediatR: `ICommand`/`IQuery`
próprios com decorators registrados por Scrutor, como no CoreMastery.

## Fluxo

1. **Escolha o módulo.** O caso de uso pertence ao módulo dono do dado que ele
   **grava**. Se grava dados de dois módulos, são dois casos de uso e um evento.
   Criar lançamento é Transactions; saldo diário é Consolidation.
2. **Classifique.** Mudança de estado é **command**; leitura é **query**. Verbo
   primeiro: `CreateTransactionCommand`, `GetDailyBalanceQuery`, `ListTransactionsQuery`.
3. **Confira o Domain.** Se o agregado ou o `{Entity}Errors` faltar, rode
   `add-entity` antes.
4. **Crie a fatia** em `src/Modules/{Module}/…Application/{Feature}/{UseCase}/`.
   Templates: [references/command-slice.md](references/command-slice.md),
   [references/query-slice.md](references/query-slice.md).
   Se o command precisa avisar outro módulo, ele grava uma `OutboxMessage` no mesmo
   `SaveChangesAsync` (ver `add-integration-event`). Nunca chama o broker.
5. **Crie o endpoint** em `…Presentation/{Feature}/{UseCase}.cs`, espelhando a
   pasta da fatia. Template: [references/endpoint.md](references/endpoint.md).
   Ele é encontrado em runtime pelo scanning; **nada** muda no `Program.cs`.
6. **Escreva os testes** — handler, validator, integração. Template:
   [references/tests.md](references/tests.md).
7. **Verifique:** `dotnet build CashFlow.slnx`, depois `dotnet test CashFlow.slnx`.
   Os testes de arquitetura passam junto com o resto.

## Convenções inegociáveis

- **Pasta = caso de uso.** Adicionar uma feature não toca arquivo compartilhado.
- **Handlers `internal sealed`** com construtor primário, implementando
  `ICommandHandler<TCommand>`, `ICommandHandler<TCommand, TResponse>` ou
  `IQueryHandler<TQuery, TResponse>`.
- **Sem registro manual no DI.** Handlers, validators e endpoints são achados por
  scanning. Editar `DependencyInjection`/`{Module}Module` para uma fatia nova
  significa que o scanning está errado.
- **`Result`/`Result<T>`, nunca exceção** para falha esperada. Erros vêm do
  `{Entity}Errors`, com a semântica certa.
- **Validação no `{Command}Validator`** e roda no decorator. O handler não
  revalida forma; ele aplica regra de negócio que depende de dados.
- **Acesso a dados pelo DbContext do próprio módulo**, no próprio schema.
- **Queries projetam para `{X}Response`** com `.Select(...)` e `AsNoTracking()`.
- **Sem `DateTime.UtcNow` na Application** — `IDateTimeProvider`.

## Regras deste projeto

- **Sem autenticação no MVP.** Não use `.RequireAuthorization()` nem `IUserContext`;
  todo endpoint é público por decisão do desafio, e isso fica documentado como
  limitação (JWT/OAuth2 é evolução). Não invente login.
- **POST de lançamento exige `Idempotency-Key`.** Mesma chave e mesmo payload:
  devolve o lançamento já criado (mesmo corpo, `201`, header `Idempotency-Replayed: true`).
  Mesma chave e payload diferente: `409` com `Transactions.IdempotencyKeyReused`.
  Corrida entre duas requisições com a mesma chave: a violação do índice único é
  traduzida pelo `SaveChangesAsync` do módulo e o handler relê e aplica a mesma regra.
- **Nenhum retry em volta do `SaveChangesAsync` do lançamento.** Retry cego em
  escrita não idempotente é proibido (regra 6). A idempotência é a key, não o Polly.
- **O POST nunca depende de Consolidation, RabbitMQ ou Worker.** Se a feature que
  você está escrevendo faz o POST esperar qualquer coisa além do PostgreSQL, ela está errada.
- **A leitura de saldo não promete sincronia com o POST.** A resposta carrega
  `lastUpdatedAt` e `consistency: "eventual"`. Data sem lançamentos devolve `200`
  com zeros e `lastUpdatedAt: null` (comportamento estável, testado).
- **Listagem paginada**: `page` ≥ 1, `pageSize` entre 1 e 200 (padrão 50),
  ordenada por `occurredAt`, `id`.
- **Limites de entrada**: `amount` > 0, até 2 casas, no máximo 999.999.999,99;
  `description` até 200; `Idempotency-Key` até 100 caracteres `[A-Za-z0-9_-]`.

## Nomes

| Artefato | Padrão | Exemplo |
|---|---|---|
| Command | `{Verb}{Entity}Command` | `CreateTransactionCommand` |
| Query | `Get{X}Query` / `List{X}Query` | `GetDailyBalanceQuery` |
| Handler | `{Command/Query}Handler` | `CreateTransactionCommandHandler` |
| Validator | `{Command}Validator` | `CreateTransactionCommandValidator` |
| Response | `{X}Response` | `DailyBalanceResponse` |
| Endpoint | `{UseCase}.cs` em `Presentation/{Feature}/` | `Presentation/Transactions/Create.cs` |
| Teste unitário | `{Handler}Tests` | `CreateTransactionCommandHandlerTests` |
| Método de teste | `Handle_Should_{Outcome}_When{Condition}` | `Handle_Should_ReturnConflict_WhenKeyReusedWithDifferentPayload` |

## Rotas do desafio

| Rota | Módulo | Fatia |
|---|---|---|
| `POST /transactions` | Transactions | `Transactions/Create` (command + Outbox) |
| `GET /transactions?date=YYYY-MM-DD&page=&pageSize=` | Transactions | `Transactions/List` |
| `GET /daily-balances/{date}` | Consolidation | `DailyBalances/Get` |

Fora disso, só com pedido explícito do usuário.
