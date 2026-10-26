# Templates de teste

Três tipos, cada um responde uma pergunta diferente: o handler decide certo? o
validator recusa o que deve? a coisa toda funciona por HTTP, com PostgreSQL de verdade?

Frameworks: xUnit + Shouldly + NSubstitute; `FluentValidation.TestHelper` para validators.
`// Arrange` / `// Act` / `// Assert` em todo teste.

## Handler (unitário)

```csharp
public class CreateTransactionCommandHandlerTests : BaseHandlerTest
{
    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenKeyReusedWithDifferentPayload()
    {
        // Arrange
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);
        await handler.Handle(Command(key: "k-1", amount: 10m), default);

        // Act
        Result<CreatedTransaction> result = await handler.Handle(Command(key: "k-1", amount: 11m), default);

        // Assert
        result.Error.ShouldBe(TransactionErrors.IdempotencyKeyReused("k-1"));
        (await context.Transactions.CountAsync()).ShouldBe(1);
        (await context.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_PersistTransactionAndOutboxMessage_WhenValid()
    {
        // ...
        result.IsSuccess.ShouldBeTrue();
        OutboxMessage message = await context.OutboxMessages.SingleAsync();
        message.Type.ShouldBe(TransactionCreatedIntegrationEvent.MessageType);
        message.PublishedOnUtc.ShouldBeNull();
    }
}
```

- Um teste por `Result.Failure`, mais o caminho feliz, mais o replay idempotente.
- Afirme o **erro de domínio exato**, não só que falhou.
- Afirme estado persistido relendo o contexto. Para comandos com Outbox, afirme
  **as duas coisas**: o lançamento e a mensagem.
- `BaseHandlerTest` cria o DbContext do módulo sobre **Sqlite em memória**
  (`DataSource=:memory:` com a conexão aberta durante o teste). Substitua
  interfaces (`IDateTimeProvider`, `IBusinessCalendar`, `ICorrelationIdProvider`)
  com NSubstitute; **nunca** mocke o DbContext.
- O que depende de SQL do PostgreSQL (`ON CONFLICT`, `FOR UPDATE SKIP LOCKED`,
  grants) **não** é testado em Sqlite: é teste de integração.

## Validator

```csharp
[Fact]
public void Validate_Should_Fail_WhenAmountHasMoreThanTwoDecimals()
{
    new CreateTransactionCommandValidator()
        .TestValidate(ValidCommand() with { Amount = 10.123m })
        .ShouldHaveValidationErrorFor(c => c.Amount);
}
```

Um teste falhando por regra, mais um command totalmente válido.

## Domínio

Testes diretos do agregado para as invariantes (valor positivo, precisão,
data de negócio) — sem DbContext, sem handler.

## Integração

```csharp
[Trait("Category", "Integration")]
public class TransactionsTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Create_Should_ReturnSameBody_WhenRepeatedWithSameKey()
    {
        // Arrange
        var body = new { type = "Credit", amount = 125.50m, occurredAt = "2026-10-26T10:00:00-03:00", description = "Venda" };

        // Act
        HttpResponseMessage first = await PostWithKeyAsync("transactions", body, key: "it-1");
        HttpResponseMessage second = await PostWithKeyAsync("transactions", body, key: "it-1");

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotency-Replayed").ShouldContain("true");
        (await ReadAsync<TransactionDto>(second)).Id.ShouldBe((await ReadAsync<TransactionDto>(first)).Id);
    }
}
```

- Caminho feliz por HTTP, com estado conferido por um GET em seguida, não
  olhando o banco.
- Tradução de erro onde o handler tem falhas: 400, 409.
- Testes de isolamento por role, Outbox, Inbox e falha ficam aqui, com containers
  reais (ver `add-integration-event` e `add-challenge`).
- Docker precisa estar rodando. Toda classe leva `[Trait("Category","Integration")]`.

## Onde não inventar expectativa

Saldo esperado é soma calculada no próprio teste a partir dos lançamentos que o
teste criou — nunca um número mágico copiado de uma execução anterior.
