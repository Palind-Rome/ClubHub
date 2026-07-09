import { readAuth } from "../authSession";

interface ApiError {
  message?: string;
  title?: string;
}

export async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, withAuthHeader(init));
  if (!res.ok) {
    let message = `请求失败（${res.status}）`;
    try {
      const body = (await res.json()) as ApiError;
      message = body.message || body.title || message;
    } catch {
      /* Keep the default error message. */
    }
    throw new Error(message);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

function withAuthHeader(init?: RequestInit): RequestInit {
  const headers = new Headers(init?.headers);
  const token = readAuth()?.token;

  if (token && !headers.has("Authorization")) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  return {
    ...init,
    headers,
  };
}
