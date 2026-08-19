import ElementPlus from "element-plus";
import { createApp, defineComponent, h, onMounted, type App as VueApp } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import App from "./App.vue";

vi.mock("./authSession", () => ({
  clearSession: vi.fn(),
  onSessionChange: vi.fn(() => () => undefined),
  readAuth: vi.fn(() => null),
}));

vi.mock("./composables/useTheme", () => ({
  initializeTheme: vi.fn(),
}));

vi.mock("./apiClient", () => ({
  apiClient: { logoutCurrentSession: vi.fn() },
}));

vi.mock("./components/shell/AppShell.vue", () => ({
  default: { name: "AppShellStub" },
}));

let mountedApp: VueApp<Element> | null = null;

beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => ({ ok: true })),
  );
});

afterEach(() => {
  mountedApp?.unmount();
  mountedApp = null;
  document.body.replaceChildren();
  vi.unstubAllGlobals();
});

describe("应用会话恢复", () => {
  it("会话恢复完成前不会挂载受保护路由组件", async () => {
    const protectedMounted = vi.fn();
    const ProtectedPage = defineComponent({
      setup() {
        onMounted(protectedMounted);
        return () => h("div", "受保护页面");
      },
    });
    const AuthPage = defineComponent({
      setup: () => () => h("div", "登录页面"),
    });
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/auth", component: AuthPage },
        { path: "/protected", component: ProtectedPage },
      ],
    });

    await router.push("/protected");
    await router.isReady();

    const host = document.createElement("div");
    document.body.appendChild(host);
    mountedApp = createApp(App);
    mountedApp.use(ElementPlus);
    mountedApp.use(router);
    mountedApp.mount(host);

    expect(protectedMounted).not.toHaveBeenCalled();
    expect(host.textContent).not.toContain("受保护页面");

    await vi.waitFor(() => {
      expect(router.currentRoute.value.path).toBe("/auth");
      expect(host.textContent).toContain("登录页面");
    });
    expect(protectedMounted).not.toHaveBeenCalled();
  });
});
