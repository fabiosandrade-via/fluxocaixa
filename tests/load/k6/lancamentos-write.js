import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';
import { Counter, Rate } from 'k6/metrics';

const acceptedRequests = new Counter('accepted_requests');
const successfulResponses = new Rate('successful_responses');
const date = __ENV.NFR_TEST_DATE || new Date().toISOString().slice(0, 10);
const rate = Number(__ENV.RATE || 50);
const duration = __ENV.DURATION || '5m';
const durationParts = /^(\d+)(s|m|h)$/.exec(duration);

if (!durationParts) {
  throw new Error('DURATION deve usar segundos, minutos ou horas, por exemplo 5m.');
}

const durationSeconds = Number(durationParts[1]) * ({ s: 1, m: 60, h: 3600 }[durationParts[2]]);

export const options = {
  scenarios: {
    lancamentos_write: {
      executor: 'constant-arrival-rate',
      rate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: 100,
      maxVUs: 400,
    },
  },
  thresholds: {
    http_req_failed: ['rate<=0.05'],
    http_req_duration: ['p(95)<=500'],
    dropped_iterations: ['count==0'],
    iterations: [`count>=${rate * durationSeconds}`],
    successful_responses: ['rate>=0.95'],
  },
  tags: {
    test_name: 'lancamentos-write',
    run_id: __ENV.RUN_ID || 'local',
  },
};

export function setup() {
  const tokenResponse = http.post(
    __ENV.OIDC_TOKEN_URL || 'http://keycloak:8080/realms/fluxocaixa/protocol/openid-connect/token',
    {
      grant_type: 'client_credentials',
      client_id: __ENV.OIDC_CLIENT_ID || 'fluxocaixa-loadtest',
      client_secret: __ENV.OIDC_CLIENT_SECRET,
    },
    { headers: { 'Content-Type': 'application/x-www-form-urlencoded' } },
  );
  if (tokenResponse.status !== 200 || !tokenResponse.json('access_token')) {
    throw new Error(`Falha ao obter token OIDC para o teste de carga: HTTP ${tokenResponse.status}`);
  }
  return { token: tokenResponse.json('access_token') };
}

export default function (data) {
  const idempotencyKey = `${__ENV.RUN_ID || 'local'}-${exec.vu.idInTest}-${exec.vu.iterationInScenario}`;
  const response = http.post(
    `${__ENV.LANCAMENTOS_URL || 'http://api-lancamentos:8080'}/api/v1/lancamentos`,
    JSON.stringify({
      tipo: 'Credito',
      valor: Number(__ENV.CREDIT_AMOUNT || 0.01),
      data: date,
      descricao: `NFR k6 ${__ENV.RUN_ID || 'local'}`,
    }),
    {
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
        Authorization: `Bearer ${data.token}`,
      },
      tags: { endpoint: 'registrar' },
    },
  );
  const successful = response.status === 201;
  successfulResponses.add(successful);
  if (successful) {
    acceptedRequests.add(1);
  }
  check(response, {
    'lançamento retorna 201': () => successful,
  });
}
