// Load test (P0): 50 RPS on GET /daily-balances/{date} for 2 minutes.
//
// Criterion fixed before the first run (docs/software/03-testing.md):
//   valid response = HTTP 200 whose JSON body is the balance of DATE, within 2 s.
//   loss = (requests without a valid response / requests sent) * 100 <= 5%.
//   5xx, timeouts and connection errors are losses; 4xx is counted apart (a
//   functional error, not expected here because DATE is a valid date).
// Latency targets are internal proposals, not from the challenge:
//   p95 < 300 ms, p99 < 800 ms.
//
// Run from the compose network so the host's port mapping is not measured:
//   docker run --rm -i --network cashflow_default -e BASE_URL=http://api:8080 \
//     -e DATE=2026-10-29 -e LABEL=worker-up -v "$PWD/tests/load:/scripts" \
//     grafana/k6 run --summary-export=/scripts/results/worker-up.json /scripts/daily-balances.js
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const DATE = __ENV.DATE || '2026-10-26';
const RATE = Number(__ENV.RATE || 50);
const DURATION = __ENV.DURATION || '2m';

const status2xx = new Counter('status_2xx');
const status4xx = new Counter('status_4xx');
const status5xx = new Counter('status_5xx');
const transportErrors = new Counter('transport_errors'); // timeout, refused, reset: no HTTP status

export const options = {
  scenarios: {
    daily_balances: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
  },
  thresholds: {
    // The challenge's criterion: at most 5% loss.
    'checks{check:valid_response}': ['rate>=0.95'],
    // Internal targets, proposed.
    http_req_duration: ['p(95)<300', 'p(99)<800'],
  },
  summaryTrendStats: ['min', 'avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  const res = http.get(`${BASE}/daily-balances/${DATE}`, { timeout: '2s', tags: { name: 'GET /daily-balances/{date}' } });

  if (res.status === 0) transportErrors.add(1);
  else if (res.status < 300) status2xx.add(1);
  else if (res.status < 500) status4xx.add(1);
  else status5xx.add(1);

  check(res, {
    valid_response: (r) => {
      if (r.status !== 200) return false;
      try {
        const body = r.json();
        return body.date === DATE && typeof body.balance === 'number' && body.consistency === 'eventual';
      } catch (_) {
        return false;
      }
    },
  }, { check: 'valid_response' });
}
