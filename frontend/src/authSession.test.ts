import { beforeEach, describe, expect, it, vi } from "vitest";
import type { AuthResponse } from "./authSession";
import {
  clearSession,
  hasCompletedSession,
  onSessionChange,
  readAuth,
  saveAuth,
} from "./authSession";

const auth = {
  token: "test-token",
  user: { id: 7, username: "student", realName: "测试学生", accountStatus: "active" },
  roles: [],
  permissions: ["club:view"],
} satisfies AuthResponse;

describe("auth session", () => {
  beforeEach(() => localStorage.clear());

  it("persists and clears a complete session", () => {
    saveAuth(auth);

    expect(readAuth()).toEqual(auth);
    expect(hasCompletedSession()).toBe(true);

    clearSession();
    expect(readAuth()).toBeNull();
    expect(hasCompletedSession()).toBe(false);
  });

  it("removes corrupted session data", () => {
    localStorage.setItem("clubhub-auth", "{broken-json");
    localStorage.setItem("clubhub-active-role", "role-1");

    expect(readAuth()).toBeNull();
    expect(localStorage.getItem("clubhub-auth")).toBeNull();
    expect(localStorage.getItem("clubhub-active-role")).toBeNull();
  });

  it("notifies listeners and supports cleanup", () => {
    const callback = vi.fn();
    const stop = onSessionChange(callback);

    saveAuth(auth);
    expect(callback).toHaveBeenCalledTimes(1);

    stop();
    clearSession();
    expect(callback).toHaveBeenCalledTimes(1);
  });
});
