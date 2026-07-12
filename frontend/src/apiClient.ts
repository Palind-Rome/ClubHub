import { Configuration, DefaultApi } from "./api";
import { clearSession, readAuth } from "./authSession";

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

export const apiClient = new DefaultApi(
  new Configuration({
    basePath: import.meta.env.VITE_API_BASE_URL ?? "",
    accessToken: () => readAuth()?.token ?? "",
    middleware: [
      {
        pre: async ({ url, init }) => ({ url, init: attachCurrentAuthorization(init) }),
        post: async ({ init, response }) => {
          if (response.status === 401) handleUnauthorizedResponse(init);
          return response;
        },
      },
    ],
  }),
);
