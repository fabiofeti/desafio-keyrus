#!/usr/bin/env bash
# Failure scenario (P0): stop the Worker, keep posting transactions, start it
# again, and check the daily balance converges to the exact sum - no loss, no
# duplicate. Then redeliver an event that was already applied and check nothing
# changes. Exits non-zero on the first broken expectation.
#
#   BASE_URL   API seen from the host   (default http://localhost:8080)
#   MGMT_URL   RabbitMQ management API  (default http://localhost:15672)
#   DATE       business date under test (default 2026-10-29)
#   N          transactions to post     (default 100)
#   TIMEOUT    seconds to converge      (default 120)
#
# Credentials are never printed: they are read from the running containers.
set -euo pipefail

BASE_URL=${BASE_URL:-http://localhost:8080}
MGMT_URL=${MGMT_URL:-http://localhost:15672}
DATE=${DATE:-2026-10-29}
N=${N:-100}
TIMEOUT=${TIMEOUT:-120}
QUEUE=consolidation.transaction-created.v1
RUN_ID=$(date +%s)

cd "$(dirname "$0")/../.."

log()  { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*"; }
fail() { log "FAIL: $*"; exit 1; }

sql() {
  docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d cashflow -tAc "$1"' _ "$1"
}

mgmt() { curl -sf -u "$RABBIT_USER:$RABBIT_PASS" -H 'content-type: application/json' "$@"; }

queue_depth() { mgmt "$MGMT_URL/api/queues/%2F/$1" | jq '.messages_ready + .messages_unacknowledged'; }

# Credits and debits in cents, as "credits debits".
balance_cents() {
  curl -sf "$BASE_URL/daily-balances/$DATE" \
    | jq -r '[.credits, .debits] | map(. * 100 | round) | "\(.[0]) \(.[1])"'
}

# Outbox events of this run's date (the payload carries the business date).
outbox_where="type = 'transactions.transaction-created.v1' AND payload->>'businessDate' = '$DATE'"

log "1. stack up"
docker compose --progress quiet up --build -d --wait api worker
log "   api healthy at $BASE_URL"
RABBIT_USER=$(docker compose exec -T rabbitmq printenv RABBITMQ_DEFAULT_USER)
RABBIT_PASS=$(docker compose exec -T rabbitmq printenv RABBITMQ_DEFAULT_PASS)

log "2. baseline for $DATE"
read -r base_credits base_debits < <(balance_cents)
base_events=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where")
log "   credits=$base_credits debits=$base_debits (cents), events so far=$base_events"

log "3. stop the worker"
docker compose --progress quiet stop worker
[[ $(docker compose ps -a worker --format '{{.State}}') == exited ]] || fail "worker still running"

log "4. post $N transactions with the worker down"
exp_credits=0; exp_debits=0; created=0
start_ms=$(date +%s%3N)
for i in $(seq 1 "$N"); do
  cents=$(( 100 + (i * 7919) % 50000 ))          # 1.00 .. 500.99, deterministic
  if (( i % 3 == 0 )); then type=Debit; exp_debits=$((exp_debits + cents)); else type=Credit; exp_credits=$((exp_credits + cents)); fi
  amount=$(printf '%d.%02d' $((cents / 100)) $((cents % 100)))
  status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/transactions" \
    -H 'content-type: application/json' -H "Idempotency-Key: chaos-$RUN_ID-$i" \
    -d "{\"type\":\"$type\",\"amount\":$amount,\"occurredAt\":\"${DATE}T12:00:00-03:00\",\"description\":\"chaos worker-down\"}")
  [[ $status == 201 ]] || fail "POST $i returned $status"
  created=$((created + 1))
done
elapsed_ms=$(( $(date +%s%3N) - start_ms ))
pending=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND published_on_utc IS NULL")
read -r mid_credits mid_debits < <(balance_cents)
log "   $created/$N POST 201 in ${elapsed_ms} ms; outbox pending for $DATE=$pending"
log "   expected delta: credits=+$exp_credits debits=+$exp_debits (cents)"
(( pending == N )) || fail "expected $N pending outbox rows, found $pending"
[[ "$mid_credits $mid_debits" == "$base_credits $base_debits" ]] || fail "balance moved with the worker down"
log "   balance unchanged while the worker is down (read still answers 200)"

log "5. start the worker and wait for convergence (timeout ${TIMEOUT}s)"
want="$((base_credits + exp_credits)) $((base_debits + exp_debits))"
docker compose --progress quiet start worker
start_s=$(date +%s)
until [[ "$(balance_cents)" == "$want" ]]; do
  (( $(date +%s) - start_s < TIMEOUT )) || fail "no convergence: have $(balance_cents), want $want"
  sleep 1
done
log "   converged in $(( $(date +%s) - start_s )) s"
sleep 5
[[ "$(balance_cents)" == "$want" ]] || fail "balance kept moving after convergence: $(balance_cents)"

log "6. exact sum, empty outbox, one inbox row per event"
pending=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND published_on_utc IS NULL")
events=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where")
inbox=$(sql "SELECT count(*) FROM consolidation.inbox_messages i JOIN transactions.outbox_messages o ON o.id = i.event_id WHERE o.$outbox_where")
log "   final credits/debits (cents)=$(balance_cents) want=$want; outbox pending=$pending; events=$events; inbox rows=$inbox"
(( pending == 0 )) || fail "outbox still has $pending pending rows"
(( events == base_events + N )) || fail "expected $((base_events + N)) events, found $events"
(( inbox == events )) || fail "inbox has $inbox rows for $events events"

log "7. redeliver an event that was already applied"
read -r event_id payload < <(sql "SELECT id, payload::text FROM transactions.outbox_messages WHERE $outbox_where ORDER BY occurred_on_utc DESC LIMIT 1" | tr '|' ' ')
dlq_before=$(queue_depth "$QUEUE.dlq")
routed=$(jq -n --arg id "$event_id" --arg p "$payload" '{
    properties: {message_id: $id, type: "transactions.transaction-created.v1", correlation_id: "chaos-redelivery", delivery_mode: 2},
    routing_key: "transaction-created.v1", payload: $p, payload_encoding: "string"}' \
  | mgmt -X POST -d @- "$MGMT_URL/api/exchanges/%2F/cashflow.transactions/publish" | jq -r .routed)
[[ $routed == true ]] || fail "redelivered message was not routed"
start_s=$(date +%s)
until (( $(queue_depth "$QUEUE") == 0 )); do
  (( $(date +%s) - start_s < 30 )) || fail "queue did not drain"
  sleep 1
done
sleep 2
inbox_after=$(sql "SELECT count(*) FROM consolidation.inbox_messages i JOIN transactions.outbox_messages o ON o.id = i.event_id WHERE o.$outbox_where")
dlq_after=$(queue_depth "$QUEUE.dlq")
log "   event $event_id redelivered: balance=$(balance_cents) inbox rows=$inbox_after dlq $dlq_before->$dlq_after"
[[ "$(balance_cents)" == "$want" ]] || fail "redelivery changed the balance"
(( inbox_after == inbox )) || fail "redelivery added an inbox row"
(( dlq_after == dlq_before )) || fail "redelivery was dead-lettered instead of deduplicated"
docker compose logs worker --since 1m 2>&1 | grep -q "Event $event_id was already applied" \
  || fail "worker did not log the redelivery as a duplicate"
log "   worker logged: Event $event_id was already applied; ignoring the redelivery"

log "PASS: $N transactions with the worker down, converged exactly, redelivery ignored"
