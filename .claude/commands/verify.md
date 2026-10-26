---
description: Build, todos os testes (inclusive integração) e validação do compose, com resumo dos resultados reais.
allowed-tools: Bash(dotnet build:*), Bash(dotnet test:*), Bash(docker compose config:*), Bash(docker info:*)
---

Rode, em ordem, parando no primeiro vermelho:

1. `dotnet build CashFlow.slnx -nologo -v q`
2. `docker info --format '{{.ServerVersion}}'` — se o Docker não responder, diga que os testes de integração não vão rodar e rode só `dotnet test CashFlow.slnx --filter "Category!=Integration"`.
3. `dotnet test CashFlow.slnx -nologo` (com Docker).
4. `docker compose config -q` (se existir `docker-compose.yml`; precisa de `.env` — se faltar, diga para copiar de `.env.example`).

Se `CashFlow.slnx` ainda não existe, diga que a solução não foi criada e sugira `/scaffold-solution`.

Responda com uma tabela curta: etapa, comando, resultado (passou/falhou, contagem de testes por projeto) e, em caso de falha, as linhas relevantes do erro. Não resuma como "tudo ok" algo que não rodou.
