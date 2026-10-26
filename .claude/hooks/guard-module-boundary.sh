#!/usr/bin/env bash
# PreToolUse(Edit|Write|MultiEdit): regra 1 do CLAUDE.md, verificada antes de gravar.
# Um módulo nunca referencia Domain/Application/Infrastructure/Presentation do outro,
# nunca cita o DbContext do outro e nunca escreve SQL contra as tabelas do outro.
# A única referência legal entre módulos é CashFlow.Modules.<Outro>.IntegrationEvents.
# Common.* não conhece módulo nenhum.
# Exit 0 = permite; exit 2 = bloqueia e devolve o stderr ao Claude.
set -uo pipefail

payload=$(cat)

file=$(jq -r '.tool_input.file_path // empty' <<<"$payload" 2>/dev/null) || exit 0
[ -z "$file" ] && exit 0

root="${CLAUDE_PROJECT_DIR:-$PWD}"
rel="${file#"$root"/}"

text=$(jq -r '[.tool_input.content, .tool_input.new_string, ((.tool_input.edits // []) | .[].new_string)]
              | map(select(. != null)) | join("\n")' <<<"$payload" 2>/dev/null) || exit 0
[ -z "$text" ] && exit 0

block() {
  {
    echo "BLOQUEADO (guard-module-boundary): $1"
    echo "Arquivo: $rel"
    echo "Trecho: $2"
    echo "Regra 1 do CLAUDE.md: um módulo não grava nas tabelas do outro nem referencia o DbContext"
    echo "ou as camadas internas dele. Comunicação entre módulos = evento via Outbox/RabbitMQ;"
    echo "a única referência permitida é CashFlow.Modules.<Outro>.IntegrationEvents."
  } >&2
  exit 2
}

check_module() {
  local other="$1" schema="$2" tables="$3" hit

  hit=$(grep -noP "CashFlow\.Modules\.${other}\.(?!IntegrationEvents\b)[A-Za-z]+" <<<"$text" | head -1)
  [ -n "$hit" ] && block "referência a camada interna do módulo ${other}" "$hit"

  hit=$(grep -noP "\bI?${other}(DbContext|UnitOfWork)\b" <<<"$text" | head -1)
  [ -n "$hit" ] && block "uso do DbContext/UnitOfWork do módulo ${other}" "$hit"

  hit=$(grep -noP "\b${schema}\.\"?(${tables})\b" <<<"$text" | head -1)
  [ -n "$hit" ] && block "SQL contra tabela do schema '${schema}'" "$hit"

  return 0
}

case "$rel" in
  src/Modules/Transactions/*)
    check_module "Consolidation" "consolidation" "daily_balances|inbox_messages|__ef_migrations_history" ;;
  src/Modules/Consolidation/*)
    check_module "Transactions" "transactions" "transactions|outbox_messages|__ef_migrations_history" ;;
  src/Common/*)
    hit=$(grep -noP "CashFlow\.Modules\.[A-Za-z]+" <<<"$text" | head -1)
    [ -n "$hit" ] && block "Common.* não pode conhecer módulos" "$hit" ;;
esac

exit 0
