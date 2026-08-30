import { Configuration, DefaultApi } from "./api";
import { clearSession, readAuth } from "./authSession";

const idempotencyKeys = new Map<string, string>();
const idempotencyFingerprintsByKey = new Map<string, string>();
const idempotentPaths = [
  /^\/api\/v1\/clubs\/applications(?:\/\d+\/reviews)?$/,
  /^\/api\/v1\/clubs\/\d+\/award-applications(?:\/\d+\/(?:submit|review))?$/,
  /^\/api\/v1\/recruitments\/\d+\/(?:reviews|applications)$/,
  /^\/api\/v1\/applications\/\d+\/reviews$/,
  /^\/api\/v1\/activities\/\d+\/(?:registrations|reviews)$/,
  /^\/api\/v1\/budget\/applications(?:\/\d+\/review)?$/,
  /^\/api\/v1\/venue-reservations(?:\/\d+\/reviews)?$/,
  /^\/api\/v1\/learning\/items\/\d+\/(?:reviews|enrollments)$/,
  /^\/api\/v1\/projects(?:\/\d+\/reviews)?$/,
  /^\/api\/v1\/projects\/\d+\/tasks\/\d+\/deliverable(?:\/review)?$/,
  /^\/api\/v1\/material-borrows(?:\/\d+\/(?:return|damage))?$/,
];

function attachCurrentAuthorization(init: RequestInit) {
  const token = readAuth()?.token;
  if (!token) return init;

  const headers = new Headers(init.headers);
  if (!headers.has("Authorization")) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  return { ...init, headers };
}

function responseMatchesCurrentSession(init: RequestInit) {
  const token = readAuth()?.token;
  if (!token) return false;

  return new Headers(init.headers).get("Authorization") === `Bearer ${token}`;
}

function handleUnauthorizedResponse(init: RequestInit) {
  if (!responseMatchesCurrentSession(init)) return;

  clearSession();
  if (typeof window === "undefined" || window.location.pathname === "/auth") return;

  const redirect = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  window.location.replace(`/auth?redirect=${encodeURIComponent(redirect)}`);
}

export function attachIdempotencyKey(url: string, init: RequestInit) {
  const parsed = new URL(url, window.location.origin);
  const method = (init.method ?? "GET").toUpperCase();
  if (["GET", "HEAD", "OPTIONS"].includes(method)) return init;
  if (!idempotentPaths.some((pattern) => pattern.test(parsed.pathname))) return init;

  const fingerprint = `${method}\n${parsed.pathname}${parsed.search}\n${String(init.body ?? "")}`;
  const key = idempotencyKeys.get(fingerprint) ?? createIdempotencyKey();
  idempotencyKeys.set(fingerprint, key);
  idempotencyFingerprintsByKey.set(key, fingerprint);
  const headers = new Headers(init.headers);
  headers.set("Idempotency-Key", key);
  return { ...init, headers };
}

export function createIdempotencyKey() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  if (typeof crypto !== "undefined" && typeof crypto.getRandomValues === "function") {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, "0"));
    return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex
      .slice(6, 8)
      .join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
  }

  return `clubhub-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 14)}`;
}

export function finishIdempotencyAttempt(init: RequestInit, response: Response) {
  if (response.status === 409 || response.status >= 500) return;
  const headers = new Headers(init.headers);
  const key = headers.get("Idempotency-Key");
  if (!key) return;
  const fingerprint = idempotencyFingerprintsByKey.get(key);
  if (fingerprint) idempotencyKeys.delete(fingerprint);
  idempotencyFingerprintsByKey.delete(key);
}

export function resetIdempotencyAttemptsForTests() {
  idempotencyKeys.clear();
  idempotencyFingerprintsByKey.clear();
}

export const apiClient = new DefaultApi(
  new Configuration({
    basePath: import.meta.env.VITE_API_BASE_URL ?? "",
    accessToken: () => readAuth()?.token ?? "",
    middleware: [
      {
        pre: async ({ url, init }) => ({
          url,
          init: attachIdempotencyKey(url, attachCurrentAuthorization(init)),
        }),
        post: async ({ init, response }) => {
          if (response.status === 401) handleUnauthorizedResponse(init);
          finishIdempotencyAttempt(init, response);
          return response;
        },
      },
    ],
  }),
);
