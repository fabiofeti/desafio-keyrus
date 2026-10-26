#!/usr/bin/env bash
# PreToolUse(Edit|Write|MultiEdit): regra 8 do CLAUDE.md - segredos fora do Git.
# Bloqueia senha literal em código, configuração, compose, Dockerfile e docs.
# Valores aceitos: referência a variável (${X}, $X), placeholder (<...>, {...}),
# vazio, ou "change-me". Fora do escopo: .env (ignorado pelo Git), .env.example
# (só placeholders, por convenção), tests/ (credenciais efêmeras de container) e .claude/.
# Exit 0 = permite; exit 2 = bloqueia e devolve o stderr ao Claude.
set -uo pipefail

payload=$(cat)

file=$(jq -r '.tool_input.file_path // empty' <<<"$payload" 2>/dev/null) || exit 0
[ -z "$file" ] && exit 0

root="${CLAUDE_PROJECT_DIR:-$PWD}"
rel="${file#"$root"/}"
base=$(basename "$rel")

case "$rel" in
  tests/*|.claude/*) exit 0 ;;
esac
case "$base" in
  .env|.env.example|.env.*) exit 0 ;;
esac

text=$(jq -r '[.tool_input.content, .tool_input.new_string, ((.tool_input.edits // []) | .[].new_string)]
              | map(select(. != null)) | join("\n")' <<<"$payload" 2>/dev/null) || exit 0
[ -z "$text" ] && exit 0

placeholder='(?![$<{]|change-?me\b|\s|;|"|'"'"'|$)'

block() {
  {
    echo "BLOQUEADO (guard-secrets): possível credencial literal em $rel"
    echo "Trecho: $1"
    echo "Regra 8 do CLAUDE.md: segredos fora do Git. Use variável de ambiente lida do .env"
    echo "(ex.: Password=\${TRANSACTIONS_APP_PASSWORD}) e deixe só placeholders no .env.example."
    echo "Se for um falso positivo (texto explicativo, não um valor), reescreva sem o padrão chave=valor."
  } >&2
  exit 2
}

# Connection string: Password=valor / Pwd=valor
hit=$(grep -noiP "\b(password|pwd)\s*=\s*${placeholder}[^;\"'\s]{3,}" <<<"$text" | head -1)
[ -n "$hit" ] && block "$hit"

# URI com credencial embutida: amqp://user:senha@host, postgres://user:senha@host
hit=$(grep -noiP "\b(amqps?|postgres(ql)?)://[^:/\s]+:${placeholder}[^@\s]+@" <<<"$text" | head -1)
[ -n "$hit" ] && block "$hit"

# Chave: valor em arquivos de configuração (JSON, YAML, Dockerfile)
case "$base" in
  *.json|*.yml|*.yaml|Dockerfile|*.Dockerfile)
    # O lookbehind ignora o nome de variável dentro de ${VAR:?msg}.
    hit=$(grep -noiP "(?<![{\$\w])\"?[A-Za-z_]*(password|passwd|secret|api[_-]?key|token|_pass)\"?\s*[:=]\s*\"?${placeholder}[^\"\s,}]{3,}" <<<"$text" | head -1)
    [ -n "$hit" ] && block "$hit" ;;
esac

exit 0
