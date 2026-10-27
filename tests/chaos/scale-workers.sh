#!/usr/bin/env bash
# Horizontal scale (P2 evidence): run W Worker replicas side by side, post N
# transactions concurrently, and check that the replicas share the work (the
# outbox rows are locked with SKIP LOCKED, the quorum queue has competing
# consumers) without publishing or applying anything twice. Exits non-zero on
# the first broken expectation; leaves the stack with one Worker.
#
#   BASE_URL  API seen from the host  (default http://localhost:8080)
#   DATE      business date under test (default 2026-11-05)
#   N         transactions             (default 300)
#   W         Worker replicas          (default 3)
#   P         concurrent POSTs         (default 20)
set -euo pipefail

BASE_URL=${BASE_URL:-http://localhost:8080}
DATE=${DATE:-2026-11-05}
N=${N:-300}
W=${W:-3}
P=${P:-20}
RUN_ID=$(date +%s)

cd "$(dirname "$0")/../.."

log()  { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*"; }
fail() { log "FAIL: $*"; docker compose --progress quiet up -d --scale worker=1 worker; exit 1; }
sql()  { docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d cashflow -tAc "$1"' _ "$1"; }
balance_cents() {
  curl -sf "$BASE_URL/daily-balances/$DATE" | jq -r '[.credits, .debits] | map(. * 100 | round) | "\(.[0]) \(.[1])"'
}
outbox_where="type = 'transactions.transaction-created.v1' AND payload->>'businessDate' = '$DATE'"

log "1. stack up with $W workers"
docker compose --progress quiet up --build -d --wait --scale worker="$W" api worker
running=$(docker compose ps worker --format '{{.State}}' | grep -c running)
(( running == W )) || fail "expected $W running workers, found $running"
since=$(date -u +%FT%TZ)

log "2. baseline for $DATE"
read -r base_credits base_debits < <(balance_cents)
base_events=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where")
log "   credits=$base_credits debits=$base_debits (cents), events so far=$base_events"

log "3. post $N transactions, $P at a time"
exp_credits=0; exp_debits=0
for i in $(seq 1 "$N"); do
  cents=$(( 100 + (i * 7919) % 50000 ))
  if (( i % 3 == 0 )); then exp_debits=$((exp_debits + cents)); else exp_credits=$((exp_credits + cents)); fi
done
start_ms=$(date +%s%3N)
statuses=$(seq 1 "$N" | xargs -P "$P" -I{} sh -c '
  i={}; cents=$(( 100 + (i * 7919) % 50000 ))
  if [ $((i % 3)) -eq 0 ]; then type=Debit; else type=Credit; fi
  amount=$(printf "%d.%02d" $((cents / 100)) $((cents % 100)))
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "'"$BASE_URL"'/transactions" \
    -H "content-type: application/json" -H "Idempotency-Key: scale-'"$RUN_ID"'-$i" \
    -d "{\"type\":\"$type\",\"amount\":$amount,\"occurredAt\":\"'"$DATE"'T12:00:00-03:00\"}"')
created=$(grep -c '^201$' <<<"$statuses" || true)
log "   $created/$N POST 201 in $(( $(date +%s%3N) - start_ms )) ms"
(( created == N )) || fail "only $created of $N POSTs returned 201"

log "4. wait for convergence"
want="$((base_credits + exp_credits)) $((base_debits + exp_debits))"
start_s=$(date +%s)
until [[ "$(balance_cents)" == "$want" ]]; do
  (( $(date +%s) - start_s < 120 )) || fail "no convergence: have $(balance_cents), want $want"
  sleep 1
done
log "   converged $(( $(date +%s) - start_s )) s after the last POST"
sleep 5
[[ "$(balance_cents)" == "$want" ]] || fail "balance kept moving: $(balance_cents)"

log "5. nothing twice, work shared"
events=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where")
pending=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND published_on_utc IS NULL")
republished=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND attempt_count > 1")
inbox=$(sql "SELECT count(*) FROM consolidation.inbox_messages i JOIN transactions.outbox_messages o ON o.id = i.event_id WHERE o.$outbox_where")
log "   events=$events pending=$pending published-more-than-once=$republished inbox=$inbox"
(( events == base_events + N )) || fail "expected $((base_events + N)) events, found $events"
(( pending == 0 )) || fail "$pending events still in the outbox"
(( inbox == events )) || fail "inbox has $inbox rows for $events events"
for c in $(docker compose ps worker --format '{{.Name}}'); do
  logs=$(docker logs --since "$since" "$c" 2>&1)
  published=$(grep -o 'Published [0-9]* outbox message' <<<"$logs" | awk '{s+=$2} END {print s+0}')
  applied=$(grep -c 'Command ApplyTransactionToDailyBalanceCommand succeeded' <<<"$logs" || true)
  duplicates=$(grep -c 'was already applied' <<<"$logs" || true)
  log "   $c: published=$published applied=$applied duplicates=$duplicates"
done

docker compose --progress quiet up -d --scale worker=1 worker
log "PASS: $W workers, $N transactions, exact sum, each event published and applied once"
