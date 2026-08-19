import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { initializeTheme, setTheme, stopThemeListener, useTheme } from "./useTheme";

function installMatchMedia(matches: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>();
  const mediaQuery = {
    matches,
    media: "(prefers-color-scheme: dark)",
    onchange: null,
    addEventListener: vi.fn((_type: string, listener: (event: MediaQueryListEvent) => void) => {
      listeners.add(listener);
    }),
    removeEventListener: vi.fn((_type: string, listener: (event: MediaQueryListEvent) => void) => {
      listeners.delete(listener);
    }),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  } as unknown as MediaQueryList;

  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => mediaQuery),
  );
  return {
    mediaQuery,
    change(nextMatches: boolean) {
      Object.defineProperty(mediaQuery, "matches", { value: nextMatches, configurable: true });
      listeners.forEach((listener) => listener({ matches: nextMatches } as MediaQueryListEvent));
    },
  };
}

describe("useTheme", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.className = "";
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.removeAttribute("data-theme-preference");
    stopThemeListener();
  });

  afterEach(() => {
    stopThemeListener();
    vi.unstubAllGlobals();
  });

  it("默认跟随系统主题并响应系统变化", () => {
    const media = installMatchMedia(false);
    initializeTheme();

    expect(document.documentElement.dataset.theme).toBe("light");
    expect(document.documentElement.dataset.themePreference).toBe("system");

    media.change(true);
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("持久化显式主题且不再跟随系统变化", () => {
    const media = installMatchMedia(false);
    const { resolvedTheme } = useTheme();

    setTheme("dark");
    expect(localStorage.getItem("clubhub-theme")).toBe("dark");
    expect(resolvedTheme.value).toBe("dark");
    expect(document.documentElement.style.colorScheme).toBe("dark");

    media.change(false);
    expect(document.documentElement.dataset.theme).toBe("dark");
  });

  it("忽略损坏的主题偏好", () => {
    localStorage.setItem("clubhub-theme", "sepia");
    installMatchMedia(true);

    initializeTheme();
    expect(document.documentElement.dataset.themePreference).toBe("system");
    expect(document.documentElement.dataset.theme).toBe("dark");
  });
});
