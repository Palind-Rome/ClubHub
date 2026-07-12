import { clearExpiredSession, readAuth } from "../authSession";

interface ApiError {
  message?: string;
  title?: string;
}

export async function requestJson<T>(
  url: string,
  init?: RequestInit,
  timeoutMs = 10000,
): Promise<T> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  const abortFromCaller = () => controller.abort();
  if (init?.signal?.aborted) {
    controller.abort();
  } else {
    init?.signal?.addEventListener("abort", abortFromCaller, { once: true });
  }

  let res: Response;
  try {
    res = await fetch(url, { ...withAuthHeader(init), signal: controller.signal });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new Error("请求超时或已取消，请稍后重试。");
    }

    throw error;
  } finally {
    clearTimeout(timeoutId);
    init?.signal?.removeEventListener("abort", abortFromCaller);
  }

  if (!res.ok) {
    if (res.status === 401 && readAuth()) {
      clearExpiredSession();
      throw new Error("登录状态已失效，请重新登录。");
    }

    let message = `请求失败（${res.status}）`;
    const text = await res.text();
    if (text) {
      try {
        const body = JSON.parse(text) as ApiError;
        message = body.message || body.title || message;
      } catch {
        /* Keep the status-based message for non-JSON responses. */
      }
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
