import http from "k6/http";
import { check } from "k6";
import { Trend } from "k6/metrics";

export const options = {
  scenarios: {
    ingest: {
      executor: "constant-arrival-rate",
      rate: __ENV.RATE ? parseInt(__ENV.RATE, 10) : 150,
      timeUnit: "1s",
      duration: __ENV.DURATION || "1m",
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
  },
};

const baseUrl = __ENV.BASE_URL || "http://localhost:5000";
const batchSize = __ENV.BATCH_SIZE ? parseInt(__ENV.BATCH_SIZE, 10) : 25;
const payloadBytes = new Trend("payload_bytes");

function uuid() {
  const s4 = () => Math.floor((1 + Math.random()) * 0x10000).toString(16).substring(1);
  return `${s4()}${s4()}-${s4()}-${s4()}-${s4()}-${s4()}${s4()}${s4()}`;
}

function buildWebSessions(now) {
  const sessions = [];
  for (let i = 0; i < batchSize; i += 1) {
    const start = new Date(now.getTime() - 60000 - i * 1000);
    const end = new Date(start.getTime() + 5000);
    sessions.push({
      sessionId: uuid(),
      domain: "example.com",
      title: "Example",
      url: "https://example.com",
      startAt: start.toISOString(),
      endAt: end.toISOString(),
    });
  }
  return sessions;
}

function buildAppSessions(now) {
  const sessions = [];
  for (let i = 0; i < batchSize; i += 1) {
    const start = new Date(now.getTime() - 60000 - i * 1000);
    const end = new Date(start.getTime() + 7000);
    sessions.push({
      sessionId: uuid(),
      processName: "Safari",
      windowTitle: "Docs",
      startAt: start.toISOString(),
      endAt: end.toISOString(),
    });
  }
  return sessions;
}

function buildIdleSessions(now) {
  const sessions = [];
  for (let i = 0; i < batchSize; i += 1) {
    const start = new Date(now.getTime() - 60000 - i * 1000);
    const end = new Date(start.getTime() + 8000);
    sessions.push({
      sessionId: uuid(),
      startAt: start.toISOString(),
      endAt: end.toISOString(),
    });
  }
  return sessions;
}

export default function () {
  const deviceId = `loadtest-${__VU}`;
  const agentVersion = "loadtest";
  const batchId = uuid();
  const sequence = __ITER;
  const sentAt = new Date().toISOString();
  const now = new Date();

  const selector = __ITER % 3;
  let url = "";
  let body = {};

  if (selector === 0) {
    url = `${baseUrl}/ingest/web-sessions`;
    body = {
      deviceId,
      agentVersion,
      batchId,
      sequence,
      sentAt,
      sessions: buildWebSessions(now),
    };
  } else if (selector === 1) {
    url = `${baseUrl}/ingest/app-sessions`;
    body = {
      deviceId,
      agentVersion,
      batchId,
      sequence,
      sentAt,
      sessions: buildAppSessions(now),
    };
  } else {
    url = `${baseUrl}/ingest/idle-sessions`;
    body = {
      deviceId,
      agentVersion,
      batchId,
      sequence,
      sentAt,
      sessions: buildIdleSessions(now),
    };
  }

  const payload = JSON.stringify(body);
  payloadBytes.add(payload.length);

  const res = http.post(url, payload, {
    headers: { "Content-Type": "application/json" },
  });

  check(res, { "status is 200": (r) => r.status === 200 });
}
