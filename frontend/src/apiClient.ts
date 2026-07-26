import { Configuration, DefaultApi } from "./api";
import { clearSession, readAuth } from "./authSession";

const idempotencyKeys = new Map<string, string>();
const idempotencyFingerprintsByKey = new Map<string, string>();
const idempotentPaths = [
  /^\/api\/clubs\/applications(?:\/\d+(?:\/review)?)?$/,
  /^\/api\/clubs\/\d+\/award-applications(?:\/\d+\/(?:submit|review))?$/,
  /^\/api\/recruitments\/(?:\d+\/(?:review|applications)|applications\/\d+\/review)$/,
  /^\/api\/activities\/\d+\/(?:registrations|review)$/,
  /^\/api\/budget\/applications(?:\/\d+\/review)?$/,
  /^\/api\/venue-reservations(?:\/\d+\/review)?$/,
  /^\/api\/learning\/items\/\d+\/(?:review|enrollments)$/,
  /^\/api\/projects(?:\/\d+\/review)?$/,
  /^\/api\/projects\/\d+\/tasks\/\d+\/deliverable(?:\/review)?$/,
  /^\/api\/material-borrows(?:\/\d+\/(?:return|damage))?$/,
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

function attachIdempotencyKey(url: string, init: RequestInit) {
  const parsed = new URL(url, window.location.origin);
  if (!idempotentPaths.some((pattern) => pattern.test(parsed.pathname))) return init;

  const fingerprint = `${init.method ?? "GET"}\n${parsed.pathname}${parsed.search}\n${String(init.body ?? "")}`;
  const key = idempotencyKeys.get(fingerprint) ?? crypto.randomUUID();
  idempotencyKeys.set(fingerprint, key);
  idempotencyFingerprintsByKey.set(key, fingerprint);
  const headers = new Headers(init.headers);
  headers.set("Idempotency-Key", key);
  return { ...init, headers };
}

function finishIdempotencyAttempt(init: RequestInit, response: Response) {
  if (response.status === 409 || response.status >= 500) return;
  const headers = new Headers(init.headers);
  const key = headers.get("Idempotency-Key");
  if (!key) return;
  const fingerprint = idempotencyFingerprintsByKey.get(key);
  if (fingerprint) idempotencyKeys.delete(fingerprint);
  idempotencyFingerprintsByKey.delete(key);
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
