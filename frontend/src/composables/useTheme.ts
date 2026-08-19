import { computed, ref } from "vue";

export type ThemePreference = "system" | "light" | "dark";
export type ResolvedTheme = Exclude<ThemePreference, "system">;

const themeStorageKey = "clubhub-theme";
const darkModeQuery = "(prefers-color-scheme: dark)";

const preference = ref<ThemePreference>("system");
const systemTheme = ref<ResolvedTheme>("light");
let initialized = false;
let mediaQuery: MediaQueryList | null = null;

function isThemePreference(value: string | null): value is ThemePreference {
  return value === "system" || value === "light" || value === "dark";
}

function resolveSystemTheme(matches: boolean): ResolvedTheme {
  return matches ? "dark" : "light";
}

function applyTheme() {
  const resolved = preference.value === "system" ? systemTheme.value : preference.value;
  const root = document.documentElement;

  root.dataset.theme = resolved;
  root.dataset.themePreference = preference.value;
  root.classList.toggle("dark", resolved === "dark");
  root.style.colorScheme = resolved;
}

function handleSystemThemeChange(event: MediaQueryListEvent | MediaQueryList) {
  systemTheme.value = resolveSystemTheme(event.matches);
  if (preference.value === "system") applyTheme();
}

export function initializeTheme() {
  if (initialized) {
    applyTheme();
    return;
  }

  const storedTheme = localStorage.getItem(themeStorageKey);
  preference.value = isThemePreference(storedTheme) ? storedTheme : "system";
  mediaQuery = window.matchMedia(darkModeQuery);
  systemTheme.value = resolveSystemTheme(mediaQuery.matches);
  mediaQuery.addEventListener("change", handleSystemThemeChange);
  initialized = true;
  applyTheme();
}

export function setTheme(nextTheme: ThemePreference) {
  preference.value = nextTheme;
  localStorage.setItem(themeStorageKey, nextTheme);
  applyTheme();
}

export function useTheme() {
  initializeTheme();

  return {
    theme: computed(() => preference.value),
    resolvedTheme: computed<ResolvedTheme>(() =>
      preference.value === "system" ? systemTheme.value : preference.value,
    ),
    setTheme,
  };
}

export function stopThemeListener() {
  mediaQuery?.removeEventListener("change", handleSystemThemeChange);
  mediaQuery = null;
  initialized = false;
}
