---
name: ca-review
description: Revisa mudanças pendentes do CashFlow contra as regras inegociáveis do CLAUDE.md e as convenções do monólito modular - fronteira entre módulos, roles de banco, Outbox/Inbox, idempotência, Polly, Result, formato das fatias, segredos e testes. Só aponta, não corrige. Use quando o usuário pedir revisão, auditoria de convenções, ou antes de marcar um item do plano como concluído.
argument-hint: [opcional: arquivos ou feature; padrão = arquivos alterados]
---

# Revisão de convenções

Escopo padrão: `git diff` + arquivos não rastreados; sem git, os arquivos que o
usuário indicar ou os alterados nesta sessão. Cada achado tem `arquivo:linha` que
você abriu. Não corrija nada sem pedido.

## Invariantes (violação = BLOCKER)

1. **Escrita cruzada.** Código, SQL ou referência de projeto de um módulo tocando
   schema, DbContext, Domain, Application ou Infrastructure do outro. Única
   referência legal: `…Transactions.IntegrationEvents`.
2. **Credencial compartilhada.** Um DbContext usando connection string de outro
   módulo, ou uma connection string única para os dois em api/worker.
3. **Outbox fora da transação.** Lançamento salvo num `SaveChanges` e mensagem em
   outro; publicação no broker dentro do request.
4. **Publicação marcada sem confirm.** `published_on_utc` gravado antes de o
   `PublishAsync` com tracking retornar; canal sem publisher confirms; mensagem não persistente.
5. **Inbox não atômica.** Check e insert separados; saldo calculado na aplicação
   (ler-somar-gravar); ACK antes do commit; `autoAck: true`.
6. **POST dependente da consolidação.** Qualquer chamada síncrona de Transactions
   para Consolidation/RabbitMQ; API dependendo do RabbitMQ no compose; circuit
   breaker no caminho do POST.
7. **Retry cego.** Polly ou loop em volta de `SaveChanges` de lançamento ou do SQL da Inbox.
8. **Segredo no repositório.** Senha literal em `appsettings*`, compose, README,
   Dockerfile ou código de `src/`.
9. **Alegação sem evidência.** Doc ou README dizendo que 50 RPS / ≤5% foi atendido
   sem a execução registrada; checkbox marcado sem teste.

## Convenções (MAJOR/MINOR conforme impacto)

- Domain só referencia `Common.Domain`; Application sem `Npgsql`/`AspNetCore`;
  Presentation sem Infrastructure nem Domain do módulo.
- Fatia: uma pasta por caso de uso, endpoint espelhando em `Presentation/{Feature}/{UseCase}.cs`,
  `IEndpoint` `internal sealed`, nada mapeado no `Program.cs`, nada registrado à mão.
- Handlers `internal sealed`, construtor primário, `Result` para falha esperada,
  erros de `{Entity}Errors` com código `"{Feature}.{Reason}"`.
- Todo command com validator; handler não revalida forma.
- Queries com `AsNoTracking()` e `.Select(...)`.
- `IDateTimeProvider` no lugar de `DateTime.UtcNow` na Application.
- `decimal` para dinheiro, `DateOnly` para data de negócio.
- Contrato v1 alterado de forma incompatível.
- Escopo: funcionalidade fora de P0/P1, ou P2 implementado sem pedido.

## Testes

- Handler novo: teste por falha + caminho feliz, afirmando estado persistido
  (lançamento **e** Outbox, quando houver).
- Validator novo: `TestValidate` por regra.
- Endpoint novo: teste de integração por HTTP.
- Outbox/Inbox/roles: testes de integração da tabela do `add-integration-event`.
- Integração marcada `[Trait("Category","Integration")]`.

## Severidade

| Severidade | Significa |
|---|---|
| **BLOCKER** | Defeito demonstrável: valor duplicado/perdido, escrita cruzada, segredo vazado, alegação falsa. Não pode seguir. |
| **MAJOR** | Convenção quebrada com consequência real provável (teste faltando num caminho de falha, camada errada). |
| **MINOR** | Desvio sem consequência imediata. |
| **OBSERVATION** | Dúvida ou sugestão; sem evidência para ser mais. |

## Formato

Primeira linha: contagem por severidade. Depois, um bloco por achado, do mais
grave ao mais leve: `SEVERIDADE` · título · `arquivo:linha` · regra em jogo ·
evidência · impacto · correção sugerida. No fim: veredito (`pode seguir` ou o que
precisa mudar antes), o que foi lido, e o resultado de `dotnet test CashFlow.slnx` se rodou.

Aponte o que está errado, não o que é só diferente do seu gosto. Na dúvida, OBSERVATION.
