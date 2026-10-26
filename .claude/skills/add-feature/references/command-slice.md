# Template de command

`src/Modules/Transactions/CashFlow.Modules.Transactions.Application/Transactions/Create/`

## O command e a resposta

```csharp
namespace CashFlow.Modules.Transactions.Application.Transactions.Create;

public sealed record CreateTransactionCommand(
    string IdempotencyKey,
    string Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description) : ICommand<CreatedTransaction>;

public sealed record TransactionResponse(
    Guid Id,
    string Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    DateOnly BusinessDate,
    string? Description);

/// <summary>Replayed diz ao endpoint para marcar a resposta; o corpo é o mesmo.</summary>
public sealed record CreatedTransaction(TransactionResponse Transaction, bool Replayed);
```

`record` `sealed` com só o que o caso de uso precisa. A idempotency key vem do
header, mas o command não sabe o que é header: o endpoint faz a tradução.

## O validator

```csharp
internal sealed class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionCommandValidator()
    {
        RuleFor(c => c.IdempotencyKey).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(c => c.Type).Must(t => Enum.TryParse<TransactionType>(t, ignoreCase: false, out _))
            .WithMessage("Type must be 'Credit' or 'Debit'.");
        RuleFor(c => c.Amount).GreaterThan(0).LessThanOrEqualTo(999_999_999.99m).PrecisionScale(18, 2, true);
        RuleFor(c => c.OccurredAt).NotEmpty();
        RuleFor(c => c.Description).MaximumLength(200);
    }
}
```

Só forma. "Essa chave já foi usada" é regra de negócio e fica no handler, onde o dado está.

## O handler

```csharp
internal sealed class CreateTransactionCommandHandler(
    ITransactionsDbContext context,
    ITransactionsUnitOfWork unitOfWork,
    IBusinessCalendar businessCalendar,
    ICorrelationIdProvider correlationIdProvider,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<CreateTransactionCommand, CreatedTransaction>
{
    public async Task<Result<CreatedTransaction>> Handle(
        CreateTransactionCommand command,
        CancellationToken cancellationToken)
    {
        string fingerprint = RequestFingerprint.Of(command);

        Result<CreatedTransaction>? replay = await TryReplayAsync(command, fingerprint, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        Result<Transaction> created = Transaction.Create(
            Enum.Parse<TransactionType>(command.Type),
            command.Amount,
            command.OccurredAt,
            businessCalendar.BusinessDateOf(command.OccurredAt),
            command.Description,
            command.IdempotencyKey,
            fingerprint,
            dateTimeProvider.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<CreatedTransaction>(created.Error);
        }

        Transaction transaction = created.Value;

        // Lançamento e Outbox no mesmo SaveChanges = mesma transação local.
        // Se o processo morrer depois do commit, o evento continua na Outbox;
        // se morrer antes, nenhum dos dois existe. Não há terceiro estado.
        var integrationEvent = TransactionCreatedIntegrationEvent.From(
            transaction, correlationIdProvider.CorrelationId, dateTimeProvider.UtcNow);

        context.Transactions.Add(transaction);
        context.OutboxMessages.Add(OutboxMessage.From(
            integrationEvent,
            TransactionCreatedIntegrationEvent.MessageType,
            correlationIdProvider.CorrelationId));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Duas requisições com a mesma chave ao mesmo tempo: a outra venceu.
            // É o único try/catch aceitável aqui, e não é retry: relê e aplica a
            // mesma regra de replay/conflito.
            return await TryReplayAsync(command, fingerprint, cancellationToken)
                ?? throw new InvalidOperationException("Unique violation without a matching idempotency key.");
        }

        return new CreatedTransaction(transaction.ToResponse(), Replayed: false);
    }

    private async Task<Result<CreatedTransaction>?> TryReplayAsync(
        CreateTransactionCommand command, string fingerprint, CancellationToken cancellationToken)
    {
        var existing = await context.Transactions
            .AsNoTracking()
            .Where(t => t.IdempotencyKey == command.IdempotencyKey)
            .Select(t => new { t.RequestFingerprint, Response = new TransactionResponse(
                t.Id, t.Type.ToString(), t.Amount, t.OccurredAt, t.BusinessDate, t.Description) })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return null;
        }

        return existing.RequestFingerprint == fingerprint
            ? new CreatedTransaction(existing.Response, Replayed: true)
            : Result.Failure<CreatedTransaction>(TransactionErrors.IdempotencyKeyReused(command.IdempotencyKey));
    }
}
```

Leia de cima para baixo: replay? → cria o agregado → agregado + Outbox → um commit.

Peças de apoio, dentro do módulo:

- `RequestFingerprint.Of(command)`: SHA-256 hex de `Type|Amount:0.00|OccurredAt:O|Description`
  com cultura invariante. A key não entra no fingerprint.
- `IBusinessCalendar` (porta na Application, implementação na Infrastructure):
  converte `OccurredAt` para o fuso `BusinessCalendar:TimeZone` (padrão
  `America/Sao_Paulo`) e devolve o `DateOnly`. Na imagem Alpine, instale `tzdata`
  no Dockerfile da API, senão `FindSystemTimeZoneById` falha em runtime.
- `UniqueConstraintViolationException` fica em `Common.Application` (sem Npgsql);
  o `SaveChangesAsync` do `TransactionsDbContext` captura `DbUpdateException` com
  `PostgresException { SqlState: "23505" }` e relança como ela.

## O que o handler não faz

- Publicar no RabbitMQ ou chamar qualquer coisa de Consolidation.
- Envolver o `SaveChangesAsync` em Polly ou em loop de retry.
- Revalidar forma que o validator já cobre.
- Chamar `DateTime.UtcNow`.
- Consultar tabela de outro módulo.
