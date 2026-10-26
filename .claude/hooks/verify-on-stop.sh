#!/usr/bin/env bash
# Stop: "a cada etapa compilar e testar" (CLAUDE.md, fluxo de trabalho).
# Se algum .cs/.csproj/.props/.slnx mudou desde a última verificação verde, roda
# build + testes rápidos (tudo menos Category=Integration) antes de deixar o turno
# terminar. Integração com containers fica no /verify e nos gates do add-challenge.
# Exit 2 = impede o fim do turno e devolve o erro ao Claude.
set -uo pipefail

payload=$(cat)
root="${CLAUDE_PROJECT_DIR:-$PWD}"
cd "$root" || exit 0

[ -f CashFlow.slnx ] || exit 0

marker=.claude/.last-verify
changed=""
if [ ! -f "$marker" ]; then
  changed="(primeira verificação)"
else
  changed=$(find src tests CashFlow.slnx Directory.Build.props Directory.Packages.props \
              -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.slnx' \) \
              -newer "$marker" -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null | head -1)
fi
[ -z "$changed" ] && exit 0

active=$(jq -r '.stop_hook_active // false' <<<"$payload" 2>/dev/null)

fail() {
  local title="$1" out="$2"
  if [ "$active" = "true" ]; then
    # Já bloqueamos uma vez neste ciclo: não prender o turno em loop. Avisa o usuário.
    jq -n --arg m "verify-on-stop: $title continua falhando. Rode /verify." '{systemMessage: $m}'
    exit 0
  fi
  {
    echo "BLOQUEADO (verify-on-stop): $title"
    echo "Corrija antes de encerrar a etapa (CLAUDE.md: a cada etapa compilar e testar)."
    echo "---"
    echo "$out" | grep -E 'error|Failed|falhou|Erro|\[FAIL\]|Assert' | head -30
    echo "---"
    echo "$out" | tail -15
  } >&2
  exit 2
}

out=$(dotnet build CashFlow.slnx -nologo -v q 2>&1) || fail "dotnet build falhou" "$out"
out=$(dotnet test CashFlow.slnx --no-build -nologo --filter "Category!=Integration" 2>&1) \
  || fail "testes unitários/arquitetura falharam" "$out"

touch "$marker"
exit 0
