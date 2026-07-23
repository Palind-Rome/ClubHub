import { beforeEach, describe, expect, it, vi } from "vitest";
import type { AuthResponse } from "../authSession";
import { readAuth, saveAuth } from "../authSession";
import { requestJson } from "./useApiRequest";

const auth = {
  token: "api-token",
  user: { id: 7, username: "student", realName: "测试学生", accountStatus: "active" },
  roles: [],
  permissions: [],
} satisfies AuthResponse;

describe("requestJson", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it("adds the session bearer token and parses JSON", async () => {
    saveAuth(auth);
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await expect(requestJson<{ ok: boolean }>("/api/example")).resolves.toEqual({ ok: true });
    const requestInit = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(new Headers(requestInit.headers).get("Authorization")).toBe("Bearer api-token");
  });

  it("preserves an explicitly supplied authorization header", async () => {
    saveAuth(auth);
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await requestJson("/api/example", { headers: { Authorization: "Bearer explicit-token" } });
    const requestInit = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(new Headers(requestInit.headers).get("Authorization")).toBe("Bearer explicit-token");
  });

  it("clears an expired session after a 401 response", async () => {
    saveAuth(auth);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    await expect(requestJson("/api/example")).rejects.toThrow("登录状态已失效");
    expect(readAuth()).toBeNull();
  });

  it("uses an API error message when one is returned", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ message: "场地不可用" }), {
          status: 409,
          headers: { "Content-Type": "application/json" },
        }),
      ),
    );

    await expect(requestJson("/api/example")).rejects.toThrow("场地不可用");
  });
});
