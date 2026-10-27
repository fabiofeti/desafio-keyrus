---
name: add-entity
description: 'Adiciona um agregado de domínio a um módulo do CashFlow - entidade com invariantes, catálogo de erros, configuração EF Core, DbContext e migration. Use quando o usuário pedir para criar uma entidade, agregado, modelo de domínio ou tabela (ex.: Transaction, DailyBalance, InboxMessage).'
argument-hint: '<descrição da entidade, ex.: "Transaction com tipo, valor, data de negócio e idempotency key">'
---

# Adicionar um agregado

O agregado é a fronteira de consistência: o que precisa estar correto depois de
cada operação. Modele comportamento, não só campos — uma classe com setters
públicos empurra as invariantes para o próximo handler que tocar nela.

## Arquivos

### 1. O agregado — `…{Module}.Domain/{Feature}/{Entity}.cs`

```csharp
namespace CashFlow.Modules.Transactions.Domain.Transactions;

public sealed class Transaction : Entity
{
    private Transaction() { }   // EF Core

    public Guid Id { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string? Description { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Transaction> Create(
        TransactionType type,
        decimal amount,
        DateTimeOffset occurredAt,
        DateOnly businessDate,
        string? description,
        string idempotencyKey,
        string requestFingerprint,
        DateTime nowUtc)
    {
        // Invariantes de domínio, independentes de HTTP: o validator cobre a forma
        // do request, o agregado garante que nenhum caminho cria um lançamento inválido.
        if (amount <= 0)
        {
            return Result.Failure<Transaction>(TransactionErrors.AmountMustBePositive);
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<Transaction>(TransactionErrors.AmountPrecision);
        }

        return new Transaction
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Amount = amount,
            OccurredAt = occurredAt,
            BusinessDate = businessDate,
            Description = description,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            CreatedAtUtc = nowUtc,
        };
    }
}
```

Setters privados, fábrica estática no lugar de construtor público, mudança de
estado só por métodos nomeados. **Lançamento é imutável**: não existe `Update`
nem `Delete` (a role de banco nem tem `DELETE`). Correção é outro lançamento.

Regras deste domínio:

- **Valor sempre positivo**; o sinal vem do `Type` (`Credit`/`Debit`). `decimal`,
  nunca `double`. Coluna `numeric(18,2)`.
- **Data de negócio explícita** (`DateOnly`), calculada uma vez, no handler, a
  partir de `OccurredAt` convertido para o fuso de negócio configurado
  (`BusinessCalendar:TimeZone`, padrão `America/Sao_Paulo`) — e gravada. Nunca
  recalculada na leitura, nunca derivada de `CreatedAtUtc`.
- **Idempotency key única** e um `RequestFingerprint` (SHA-256 do payload
  normalizado) para distinguir "mesma requisição repetida" de "mesma chave, outro payload".

### 2. Catálogo de erros — `…Domain/{Feature}/{Entity}Errors.cs`

```csharp
public static class TransactionErrors
{
    public static readonly Error AmountMustBePositive = Error.Problem(
        "Transactions.AmountMustBePositive", "The amount must be greater than zero.");

    public static readonly Error AmountPrecision = Error.Problem(
        "Transactions.AmountPrecision", "The amount must have at most two decimal places.");

    public static Error IdempotencyKeyReused(string key) => Error.Conflict(
        "Transactions.IdempotencyKeyReused",
        $"The Idempotency-Key '{key}' was already used with a different payload.");
}
```

Códigos `"{FeaturePlural}.{Reason}"`. Semântica: `NotFound` (404), `Conflict` (409),
`Problem` (400), `Failure` (500).

### 3. Configuração EF — `…Infrastructure/{Feature}/{Entity}Configuration.cs`

```csharp
internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Description).HasMaxLength(200);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(100);
        builder.Property(t => t.RequestFingerprint).HasMaxLength(64);

        builder.HasIndex(t => t.IdempotencyKey).IsUnique();
        builder.HasIndex(t => t.BusinessDate);   // GET /transactions?date=
    }
}
```

Persistência só aqui, nunca atributos na entidade. `ApplyConfigurationsFromAssembly`
encontra a classe.

### 4. DbContext

`DbSet<{Entity}>` em `I{Module}DbContext` e na implementação.

### 5. Migration

```bash
# O design-time sobe o host da API, que exige a connection string de cada
# módulo; um valor fictício basta (nenhuma conexão é aberta para gerar).
ConnectionStrings__Transactions="Host=localhost;Database=design" \
ConnectionStrings__Consolidation="Host=localhost;Database=design" \
dotnet ef migrations add Add_Transactions \
  --project src/Modules/Transactions/CashFlow.Modules.Transactions.Infrastructure \
  --startup-project src/Api/CashFlow.Api \
  --context TransactionsDbContext \
  --output-dir Database/Migrations
```

Nome `PascalCase_With_Underscores`. Se o `dotnet ef` não estiver instalado:
`dotnet tool install --global dotnet-ef` (peça ao usuário antes). Gere a migration
pelo comando; **nunca** edite o `ModelSnapshot` à mão. Cada módulo tem a própria
pasta de migrations e o próprio histórico; gerar duas migrations em paralelo no
mesmo contexto quebra o snapshot. Depois de gerar, confira com
`dotnet ef migrations has-pending-model-changes` (mesmos parâmetros e variáveis).

## Os agregados que este desafio precisa

| Módulo | Tabela | Chave | Observação |
|---|---|---|---|
| Transactions | `transactions.transactions` | `id uuid` | `idempotency_key` única, índice em `business_date` |
| Transactions | `transactions.outbox_messages` | `id uuid` (= eventId) | Config de `Common.Infrastructure`; índice parcial nos não publicados |
| Consolidation | `consolidation.daily_balances` | `business_date date` | `credits`, `debits` `numeric(18,2)`, `updated_at`; saldo = `credits - debits`, calculado |
| Consolidation | `consolidation.inbox_messages` | `event_id uuid` | `message_type`, `processed_at` |

`DailyBalance` é escrito **somente** pelo SQL atômico da Inbox (ver
`add-integration-event`); a entidade existe para leitura e para o mapeamento.

## Regras

- **Domain referencia só `Common.Domain`** — sem EF Core, sem Application.
- **Sem FK nem navegação para outro schema.** Um id de outro módulo é só um valor.
- **Value object** quando a regra importa (ex.: `Money` que não aceita negativo)
  — mas não crie VO que nenhum caso de uso exige.
- `dotnet build` e `dotnet test` ao terminar. Continue com `add-feature` se o
  agregado precisar de casos de uso.
