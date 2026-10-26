# Instruções para Claude Code — Desafio Keyrus

## Objetivo
Implementar **hoje** uma prova técnica pequena, funcional e testável para dois perfis: Arquiteto de Software e Arquiteto de Soluções. Leia primeiro `README.md`, `docs/IMPLEMENTATION-PLAN.md` e `docs/adr/ADR-001.md`.

## Stack e limites
- C# / .NET 10, ASP.NET Core Minimal APIs, EF Core, PostgreSQL 17 Alpine, RabbitMQ (imagem management), Polly, xUnit.
- Monólito modular: uma API e um Worker (dois processos/deployables, módulos de domínio compartilhados sem duplicação).
- Dois bounded contexts: **Transactions** e **Consolidation**. Cada um possui DbContext, migrations, schema e credenciais de banco próprias. Evitar complexidade que não contribua ao desafio.
- Sem Kafka, Kubernetes, CQRS framework, MediatR obrigatório, autenticação corporativa, frontend ou infraestrutura cloud real no MVP.
- Docker Compose deve subir API, worker, RabbitMQ e PostgreSQL; configurações via variáveis de ambiente.

## Regras inegociáveis
1. Um módulo jamais grava diretamente nas tabelas do outro, nem referencia seu DbContext.
2. Persistência no próprio módulo; comunicação de mudanças entre módulos por eventos/comandos via RabbitMQ, recebidos e persistidos pelo módulo proprietário.
3. Transactions grava lançamento e mensagem Outbox na mesma transação local. Publisher em background entrega com retry; publicação marcada somente após confirmação do broker.
4. Consolidation usa Inbox/deduplicação e atualização atômica de saldo; redelivery não pode duplicar valores.
5. Falha no consumidor/consolidação não impede POST de lançamentos. Falha compartilhada do PostgreSQL é limitação conhecida.
6. Polly em chamadas com falhas transitórias (especialmente publicação/conexão com broker), com limites, backoff e circuit breaker apropriado; não aplicar retry cego a escrita não idempotente.
7. Não alegar que o requisito de 50 RPS e até 5% de perda foi atendido sem executar teste mensurável. Registrar resultados reais.
8. Segredos fora do Git; nunca adicionar credenciais reais ao README.
9. Manter documentação e código consistentes; atualizar status e evidências.

## Fluxo de trabalho
Implementar em pequenas etapas: (1) solução e compose; (2) Transactions + migração; (3) Outbox/RabbitMQ; (4) Consolidation + Inbox; (5) endpoints de leitura; (6) testes; (7) carga/falha; (8) documentação e validação final. A cada etapa compilar e testar. Não expandir escopo sem necessidade. Ao terminar, apresentar comandos executados, resultados, limitações e links relativos para documentação.
