import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '5s', target: 5 },     // Phase 1: Gentle Warm-up (Hydrates the Cache)
    { duration: '10s', target: 1000 }, // Phase 2: Ramp up to 1,000 concurrent VUs
    { duration: '20s', target: 1000 }, // Phase 3: Hold steady under heavy load
    { duration: '5s', target: 0 },     // Phase 4: Cool down
  ],
  thresholds: {
    // We expect 95% of requests to clear in under 50ms now that the CPU can breathe
    http_req_duration: ['p(95)<50'],
  },
};

export default function () {
  const res = http.get('http://localhost:5000/api/sales/summary?region=EU');

  check(res, {
    'status is 200': (r) => r.status === 200
  });

  // CRITICAL: Prevent local CPU exhaustion by simulating a 100ms network/client delay
  sleep(0.1);
}