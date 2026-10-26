#!/usr/bin/env bash
# Schemas and least-privilege runtime roles. Runs once, when the PostgreSQL
# volume is empty (official image behaviour); the integration tests mount this
# same file, so the isolation they test is the real one.
#
# Tables are created later by the migrations, as the administrative user
# (POSTGRES_USER). The DEFAULT PRIVILEGES below are what give each app role
# access to its own module's future tables - and to nothing else.
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

-- Each role sees only its own schema. No DELETE: no flow deletes anything.
GRANT USAGE ON SCHEMA transactions TO transactions_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA transactions
  GRANT SELECT, INSERT, UPDATE ON TABLES TO transactions_app;

GRANT USAGE ON SCHEMA consolidation TO consolidation_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA consolidation
  GRANT SELECT, INSERT, UPDATE ON TABLES TO consolidation_app;
SQL
