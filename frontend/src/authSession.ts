import type { AuthResponse } from "./api/models";

export type { AuthUser, AuthRole, AuthResponse } from "./api/models";

const authKey = "clubhub-auth";
const roleKeyName = "clubhub-active-role";
const sessionEvent = "clubhub-session-change";

export function readAuth(): AuthResponse | null {
  const raw = localStorage.getItem(authKey);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as AuthResponse;
  } catch {
    localStorage.removeItem(authKey);
    localStorage.removeItem(roleKeyName);
    return null;
  }
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
