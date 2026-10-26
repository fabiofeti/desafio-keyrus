#!/usr/bin/env bash
# PostToolUse(Edit|Write|MultiEdit): edições que compilam e ainda assim estão erradas.
# Só avisa (nunca bloqueia): devolve o lembrete ao Claude como additionalContext.
set -uo pipefail

payload=$(cat)

file=$(jq -r '.tool_input.file_path // empty' <<<"$payload" 2>/dev/null) || exit 0
[ -z "$file" ] && exit 0

root="${CLAUDE_PROJECT_DIR:-$PWD}"
rel="${file#"$root"/}"
text=$(jq -r '[.tool_input.content, .tool_input.new_string, ((.tool_input.edits // []) | .[].new_string)]
              | map(select(. != null)) | join("\n")' <<<"$payload" 2>/dev/null)

notes=()

case "$rel" in
  src/Modules/*/Migrations/*ModelSnapshot.cs|src/Modules/*/Database/Migrations/*ModelSnapshot.cs)
    notes+=("O ModelSnapshot foi editado à mão. Ele é gerado por 'dotnet ef migrations add'; edição manual faz a próxima migration divergir do banco (add-entity §5).") ;;
  src/Modules/*/Migrations/*|src/Modules/*/Database/Migrations/*) ;;
  src/Modules/*.Domain/*.cs|src/Modules/*Configuration.cs|src/Modules/*DbContext.cs)
    notes+=("O modelo EF de um módulo pode ter mudado. Se mudou coluna, tabela ou índice, gere a migration do próprio módulo com 'dotnet ef migrations add … --context <Módulo>DbContext' (add-entity §5).") ;;
esac

case "$rel" in
  src/Modules/*.IntegrationEvents/*.cs)
    notes+=("Contrato público entre módulos alterado. Campo novo opcional é compatível; remover, renomear ou mudar significado exige um v2 publicado ao lado do v1 (add-integration-event §1).") ;;
esac

case "$rel" in
  docs/IMPLEMENTATION-PLAN.md)
    if grep -q '\[x\]' <<<"$text"; then
      notes+=("Um item do plano foi marcado como concluído. Confirme que os gates 1-3 do add-challenge rodaram e que a evidência real está em docs/software/03-testing.md (regras 7 e 9).")
    fi ;;
esac

case "$rel" in
  docker-compose*.yml)
    if awk '/^  api:/{a=1;next} /^  [a-z]/{a=0} a' "$file" 2>/dev/null | grep -q 'rabbitmq'; then
      notes+=("O serviço api parece depender do rabbitmq no compose. A API não fala com o broker; esse depends_on faz o POST cair junto com o RabbitMQ (regra 5).")
    fi ;;
esac

[ ${#notes[@]} -eq 0 ] && exit 0

msg=$(printf '%s\n' "${notes[@]}")
jq -n --arg m "$msg" '{hookSpecificOutput: {hookEventName: "PostToolUse", additionalContext: $m}}'
exit 0
