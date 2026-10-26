# Templates de infraestrutura local

## deploy/postgres/init/01-roles.sh

Roda uma única vez, quando o volume do PostgreSQL está vazio (comportamento da
imagem oficial). Os mesmos arquivos são montados pelos testes de integração.

```bash
#!/usr/bin/env bash
# Schemas e roles de runtime com privilégio mínimo. As tabelas são criadas depois,
# pelas migrations, com o usuário administrativo (POSTGRES_USER); os DEFAULT
# PRIVILEGES abaixo é que dão às roles de app acesso a essas tabelas futuras.
set -euo pipefail

: "${TRANSACTIONS_APP_PASSWORD:?TRANSACTIONS_APP_PASSWORD is required}"
: "${CONSOLIDATION_APP_PASSWORD:?CONSOLIDATION_APP_PASSWORD is required}"

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v tx_pwd="$TRANSACTIONS_APP_PASSWORD" \
  -v co_pwd="$CONSOLIDATION_APP_PASSWORD" <<'SQL'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

CREATE SCHEMA IF NOT EXISTS transactions;
CREATE SCHEMA IF NOT EXISTS consolidation;

CREATE ROLE transactions_app LOGIN PASSWORD :'tx_pwd'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
CREATE ROLE consolidation_app LOGIN PASSWORD :'co_pwd'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;

-- Cada role enxerga apenas o próprio schema. Sem DELETE: nenhum fluxo apaga.
GRANT USAGE ON SCHEMA transactions TO transactions_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA transactions
  GRANT SELECT, INSERT, UPDATE ON TABLES TO transactions_app;

GRANT USAGE ON SCHEMA consolidation TO consolidation_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA consolidation
  GRANT SELECT, INSERT, UPDATE ON TABLES TO consolidation_app;
SQL
```

Teste de integração obrigatório (P0): com a role `transactions_app`, um
`INSERT INTO consolidation.daily_balances …` falha com `42501 permission denied`;
e o inverso com `consolidation_app` em `transactions.transactions`.

## Dockerfiles

`src/Api/Dockerfile` (o do Worker é igual, trocando o projeto, a imagem final
para `mcr.microsoft.com/dotnet/runtime:10.0-alpine` e sem porta HTTP):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# global.json fica de fora: a feature band do SDK da imagem pode ser menor que a local.
COPY Directory.Build.props Directory.Packages.props CashFlow.slnx ./
COPY src/ src/
RUN dotnet publish src/Api/CashFlow.Api/CashFlow.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# tzdata: a data de negócio é calculada no fuso America/Sao_Paulo (IBusinessCalendar).
RUN apk add --no-cache tzdata
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CashFlow.Api.dll"]
```

Se o restore do csproj por camada for desejado (cache), copie primeiro só os
`*.csproj` mantendo a árvore; é otimização, não requisito.

## docker-compose.yml

```yaml
name: cashflow

x-dotnet-env: &dotnet-env
  DOTNET_ENVIRONMENT: Production
  ASPNETCORE_ENVIRONMENT: Production

services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: cashflow
      POSTGRES_USER: ${POSTGRES_USER:?set in .env}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set in .env}
      TRANSACTIONS_APP_PASSWORD: ${TRANSACTIONS_APP_PASSWORD:?set in .env}
      CONSOLIDATION_APP_PASSWORD: ${CONSOLIDATION_APP_PASSWORD:?set in .env}
    volumes:
      - pgdata:/var/lib/postgresql/data
      - ./deploy/postgres/init:/docker-entrypoint-initdb.d:ro
    ports: ["5432:5432"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d cashflow"]
      interval: 5s
      timeout: 3s
      retries: 20

  rabbitmq:
    image: rabbitmq:4-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER:?set in .env}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?set in .env}
    volumes:
      - rabbitdata:/var/lib/rabbitmq
    ports: ["5672:5672", "15672:15672"]
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 10s
      timeout: 5s
      retries: 12

  # Bootstrap: a única coisa que usa credencial administrativa do banco.
  migrator:
    build: { context: ., dockerfile: src/Api/Dockerfile }
    command: ["--migrate"]
    environment:
      <<: *dotnet-env
      ConnectionStrings__Transactions: Host=postgres;Database=cashflow;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
      ConnectionStrings__Consolidation: Host=postgres;Database=cashflow;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
    depends_on:
      postgres: { condition: service_healthy }
    restart: "no"

  api:
    build: { context: ., dockerfile: src/Api/Dockerfile }
    environment:
      <<: *dotnet-env
      ConnectionStrings__Transactions: Host=postgres;Database=cashflow;Username=transactions_app;Password=${TRANSACTIONS_APP_PASSWORD}
      ConnectionStrings__Consolidation: Host=postgres;Database=cashflow;Username=consolidation_app;Password=${CONSOLIDATION_APP_PASSWORD}
    ports: ["8080:8080"]
    depends_on:
      migrator: { condition: service_completed_successfully }
    healthcheck:
      test: ["CMD", "wget", "-qO-", "http://localhost:8080/health"]
      interval: 10s
      timeout: 3s
      retries: 10
    restart: unless-stopped

  worker:
    build: { context: ., dockerfile: src/Worker/Dockerfile }
    environment:
      <<: *dotnet-env
      ConnectionStrings__Transactions: Host=postgres;Database=cashflow;Username=transactions_app;Password=${TRANSACTIONS_APP_PASSWORD}
      ConnectionStrings__Consolidation: Host=postgres;Database=cashflow;Username=consolidation_app;Password=${CONSOLIDATION_APP_PASSWORD}
      RabbitMq__Host: rabbitmq
      RabbitMq__Username: ${RABBITMQ_USER}
      RabbitMq__Password: ${RABBITMQ_PASSWORD}
    depends_on:
      migrator: { condition: service_completed_successfully }
      rabbitmq: { condition: service_healthy }
    restart: unless-stopped

volumes:
  pgdata:
  rabbitdata:
```

A API **não** depende do RabbitMQ no compose. Isso é proposital e é o que
permite o cenário "broker fora, POST continua".

## .env.example

```dotenv
# Copie para .env (ignorado pelo Git) e troque os valores. Nunca commite o .env.
POSTGRES_USER=cashflow_admin
POSTGRES_PASSWORD=change-me
TRANSACTIONS_APP_PASSWORD=change-me
CONSOLIDATION_APP_PASSWORD=change-me
RABBITMQ_USER=cashflow
RABBITMQ_PASSWORD=change-me
```

## .gitignore (mínimo)

```gitignore
bin/
obj/
.env
*.user
.vs/
.idea/
TestResults/
.claude/settings.local.json
.claude/.last-verify
tests/load/results/*.json
```
