import { Configuration, DefaultApi } from "./api";
import { clearSession, readAuth } from "./authSession";

function handleUnauthorizedResponse() {
  if (!readAuth()) return;

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
        post: async ({ response }) => {
          if (response.status === 401) handleUnauthorizedResponse();
          return response;
        },
      },
    ],
  }),
);
