import type { AuthResponse } from "./api/models";

export type { AuthUser, AuthRole, AuthResponse } from "./api/models";

const authKey = "clubhub-auth";
const roleKeyName = "clubhub-active-role";
const sessionEvent = "clubhub-session-change";

export function readAuth(): AuthResponse | null {
  const raw = localStorage.getItem(authKey);
  if (!raw) return null;

  try {
    const parsed: unknown = JSON.parse(raw);
    if (isAuthResponse(parsed)) return parsed;
    removeStoredSession();
    return null;
  } catch {
    removeStoredSession();
    return null;
  }
}

function isAuthResponse(value: unknown): value is AuthResponse {
  if (!isRecord(value) || typeof value.token !== "string") return false;
  if (!isRecord(value.user)) return false;
  if (
    typeof value.user.id !== "number" ||
    typeof value.user.username !== "string" ||
    typeof value.user.realName !== "string" ||
    typeof value.user.accountStatus !== "string"
  ) {
    return false;
  }
  if (!Array.isArray(value.permissions) || !value.permissions.every(isString)) return false;
  if (!Array.isArray(value.roles) || !value.roles.every(isAuthRole)) return false;
  return true;
}

function isAuthRole(value: unknown) {
  return (
    isRecord(value) &&
    typeof value.id === "number" &&
    typeof value.code === "string" &&
    typeof value.name === "string" &&
    typeof value.displayName === "string" &&
    (value.scope === "system" || value.scope === "club") &&
    Array.isArray(value.clubIds) &&
    value.clubIds.every((clubId) => typeof clubId === "number") &&
    Array.isArray(value.permissions) &&
    value.permissions.every(isString)
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isString(value: unknown): value is string {
  return typeof value === "string";
}

function removeStoredSession() {
  localStorage.removeItem(authKey);
  localStorage.removeItem(roleKeyName);
}

export function saveAuth(auth: AuthResponse) {
  localStorage.setItem(authKey, JSON.stringify(auth));
  localStorage.removeItem(roleKeyName);
  notifySessionChange();
}

export function clearSession() {
  localStorage.removeItem(authKey);
  localStorage.removeItem(roleKeyName);
  notifySessionChange();
}

export function clearExpiredSession() {
  if (readAuth()) {
    clearSession();
  }
}

export function hasCompletedSession() {
  return Boolean(readAuth());
}

export function onSessionChange(callback: () => void) {
  window.addEventListener(sessionEvent, callback);
  window.addEventListener("storage", callback);
  return () => {
    window.removeEventListener(sessionEvent, callback);
    window.removeEventListener("storage", callback);
  };
}

function notifySessionChange() {
  window.dispatchEvent(new Event(sessionEvent));
}
