#!/usr/bin/env bash
# Failure scenario (P1): stop RabbitMQ, keep posting transactions, start it
# again, and check that the API never noticed, the outbox held every event,
# the Worker survived and reconnected (publisher and consumer), and the daily
# balance converges to the exact sum. Exits non-zero on the first broken
# expectation.
#
#   BASE_URL   API seen from the host   (default http://localhost:8080)
#   MGMT_URL   RabbitMQ management API  (default http://localhost:15672)
#   DATE       business date under test (default 2026-10-31)
#   N          transactions to post     (default 100)
#   OUTAGE     extra seconds the broker stays down after the POSTs (default 30)
#   TIMEOUT    seconds to converge      (default 180)
#
# Credentials are never printed: they are read from the running containers.
set -euo pipefail

BASE_URL=${BASE_URL:-http://localhost:8080}
MGMT_URL=${MGMT_URL:-http://localhost:15672}
DATE=${DATE:-2026-10-31}
N=${N:-100}
OUTAGE=${OUTAGE:-30}
TIMEOUT=${TIMEOUT:-180}
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

outbox_where="type = 'transactions.transaction-created.v1' AND payload->>'businessDate' = '$DATE'"

log "1. stack up"
docker compose --progress quiet up --build -d --wait api worker
RABBIT_USER=$(docker compose exec -T rabbitmq printenv RABBITMQ_DEFAULT_USER)
RABBIT_PASS=$(docker compose exec -T rabbitmq printenv RABBITMQ_DEFAULT_PASS)
log "   api healthy at $BASE_URL"

log "2. baseline for $DATE"
read -r base_credits base_debits < <(balance_cents)
base_events=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where")
dlq_before=$(queue_depth "$QUEUE.dlq")
log "   credits=$base_credits debits=$base_debits (cents), events so far=$base_events, dlq=$dlq_before"

log "3. stop the broker"
outage_start=$(date -u +%FT%TZ)
docker compose --progress quiet stop rabbitmq
[[ $(docker compose ps -a rabbitmq --format '{{.State}}') == exited ]] || fail "rabbitmq still running"

log "4. post $N transactions with the broker down"
exp_credits=0; exp_debits=0; created=0; slowest=0
start_ms=$(date +%s%3N)
for i in $(seq 1 "$N"); do
  cents=$(( 100 + (i * 7919) % 50000 ))          # 1.00 .. 500.99, deterministic
  if (( i % 3 == 0 )); then type=Debit; exp_debits=$((exp_debits + cents)); else type=Credit; exp_credits=$((exp_credits + cents)); fi
  amount=$(printf '%d.%02d' $((cents / 100)) $((cents % 100)))
  read -r status seconds < <(curl -s -o /dev/null -w '%{http_code} %{time_total}\n' -X POST "$BASE_URL/transactions" \
    -H 'content-type: application/json' -H "Idempotency-Key: chaos-broker-$RUN_ID-$i" \
    -d "{\"type\":\"$type\",\"amount\":$amount,\"occurredAt\":\"${DATE}T12:00:00-03:00\",\"description\":\"chaos broker-down\"}")
  [[ $status == 201 ]] || fail "POST $i returned $status"
  ms=$(awk -v s="$seconds" 'BEGIN { printf "%d", s * 1000 }')
  (( ms > slowest )) && slowest=$ms
  created=$((created + 1))
done
elapsed_ms=$(( $(date +%s%3N) - start_ms ))
log "   $created/$N POST 201 in ${elapsed_ms} ms (slowest ${slowest} ms)"
log "   expected delta: credits=+$exp_credits debits=+$exp_debits (cents)"

log "5. keep the broker down for ${OUTAGE}s more"
sleep "$OUTAGE"
pending=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND published_on_utc IS NULL")
attempted=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND attempt_count > 0 AND last_error IS NOT NULL")
worker_state=$(docker compose ps -a worker --format '{{.State}}')
read -r mid_credits mid_debits < <(balance_cents)
logs=$(docker compose logs worker --since "$outage_start" 2>&1)
circuit_open=$(grep -c "broker circuit is open" <<<"$logs" || true)
publish_failed=$(grep -c "Outbox publishing failed\|failed to publish\|Publishing outbox message" <<<"$logs" || true)
log "   outbox pending for $DATE=$pending; rows with a recorded failed attempt=$attempted; worker=$worker_state"
log "   worker log during outage: 'circuit is open' x$circuit_open, publish failures x$publish_failed"
(( pending == N )) || fail "expected $N pending outbox rows, found $pending"
[[ $worker_state == running ]] || fail "worker is $worker_state during the outage"
[[ "$mid_credits $mid_debits" == "$base_credits $base_debits" ]] || fail "balance moved with the broker down"
(( attempted >= 1 )) || fail "the publisher never recorded a failed attempt"

log "6. start the broker and wait for convergence (timeout ${TIMEOUT}s)"
want="$((base_credits + exp_credits)) $((base_debits + exp_debits))"
recovery_start=$(date -u +%FT%TZ)
docker compose --progress quiet start rabbitmq
start_s=$(date +%s)
until [[ "$(balance_cents)" == "$want" ]]; do
  (( $(date +%s) - start_s < TIMEOUT )) || fail "no convergence: have $(balance_cents), want $want"
  sleep 1
done
log "   converged $(( $(date +%s) - start_s )) s after 'start rabbitmq'"
sleep 5
[[ "$(balance_cents)" == "$want" ]] || fail "balance kept moving after convergence: $(balance_cents)"

log "7. exact sum, empty outbox, one inbox row per event, consumer back"
pending=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where AND published_on_utc IS NULL")
events=$(sql "SELECT count(*) FROM transactions.outbox_messages WHERE $outbox_where")
inbox=$(sql "SELECT count(*) FROM consolidation.inbox_messages i JOIN transactions.outbox_messages o ON o.id = i.event_id WHERE o.$outbox_where")
max_attempts=$(sql "SELECT max(attempt_count) FROM transactions.outbox_messages WHERE $outbox_where")
# Inbox commit comes just before the ACK, and the management API samples queue
# stats every few seconds: wait for the queue to drain instead of reading once.
drain_s=$(date +%s)
until (( $(queue_depth "$QUEUE") == 0 )); do
  (( $(date +%s) - drain_s < 30 )) || fail "queue still holds $(queue_depth "$QUEUE") messages"
  sleep 1
done
depth=$(queue_depth "$QUEUE")
dlq_after=$(queue_depth "$QUEUE.dlq")
resubscribed=$(docker compose logs worker --since "$recovery_start" 2>&1 | grep -c "Consuming .* from $QUEUE" || true)
log "   final credits/debits (cents)=$(balance_cents) want=$want"
log "   outbox pending=$pending; events=$events; inbox rows=$inbox; max attempt_count=$max_attempts"
log "   queue drained in $(( $(date +%s) - drain_s )) s, depth=$depth; dlq $dlq_before->$dlq_after; consumer re-subscribed x$resubscribed"
(( pending == 0 )) || fail "outbox still has $pending pending rows"
(( events == base_events + N )) || fail "expected $((base_events + N)) events, found $events"
(( inbox == events )) || fail "inbox has $inbox rows for $events events"
(( dlq_after == dlq_before )) || fail "messages were dead-lettered during recovery"
(( resubscribed >= 1 )) || fail "consumer did not log a new subscription after the broker came back"

log "PASS: $N transactions with the broker down, API unaffected, outbox drained, converged exactly"
