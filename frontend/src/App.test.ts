import ElementPlus from "element-plus";
import { createApp, defineComponent, h, onMounted, type App as VueApp } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import App from "./App.vue";

const mocks = vi.hoisted(() => ({
  clearSession: vi.fn(),
  onSessionChange: vi.fn(() => () => undefined),
  readAuth: vi.fn(() => null as unknown),
  saveAuth: vi.fn(),
  refreshAuthSession: vi.fn(),
  logoutCurrentSession: vi.fn(),
}));

vi.mock("./authSession", () => ({
  clearSession: mocks.clearSession,
  onSessionChange: mocks.onSessionChange,
  readAuth: mocks.readAuth,
  saveAuth: mocks.saveAuth,
}));

vi.mock("./composables/useTheme", () => ({
  initializeTheme: vi.fn(),
}));

vi.mock("./apiClient", () => ({
  apiClient: {
    logoutCurrentSession: mocks.logoutCurrentSession,
    refreshAuthSession: mocks.refreshAuthSession,
  },
}));

vi.mock("./components/shell/AppShell.vue", () => ({
  default: { name: "AppShellStub" },
}));

let mountedApp: VueApp<Element> | null = null;

beforeEach(() => {
  mocks.readAuth.mockReset();
  mocks.readAuth.mockReturnValue(null);
  mocks.saveAuth.mockReset();
  mocks.refreshAuthSession.mockReset();
  mocks.onSessionChange.mockReset();
  mocks.onSessionChange.mockReturnValue(() => undefined);
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

  it("启动时从服务端刷新缓存角色，避免展示过期社团身份", async () => {
    const staleAuth = {
      token: "stale-token",
      user: { id: 8, username: "club_admin", realName: "王夫人", accountStatus: "normal" },
      roles: [
        {
          id: 6,
          code: "ADVISOR",
          name: "指导老师",
          displayName: "图灵社指导老师",
          scope: "club",
          clubId: 1000003,
          clubIds: [1000003],
          permissions: ["club:internal:view"],
        },
      ],
      permissions: ["club:internal:view"],
    };
    const currentAuth = {
      ...staleAuth,
      token: "current-token",
      roles: [
        {
          ...staleAuth.roles[0],
          code: "CLUB_ADMIN",
          name: "社团管理员",
          displayName: "社团管理员",
          scope: "system",
          clubId: null,
          clubIds: [],
          permissions: ["club:review"],
        },
      ],
      permissions: ["club:review"],
    };
    mocks.readAuth.mockReturnValue(staleAuth);
    mocks.refreshAuthSession.mockResolvedValue(currentAuth);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: "/dashboard", component: { template: "<div />" } }],
    });
    await router.push("/dashboard");
    await router.isReady();

    const host = document.createElement("div");
    document.body.appendChild(host);
    mountedApp = createApp(App);
    mountedApp.use(ElementPlus);
    mountedApp.use(router);
    mountedApp.mount(host);

    await vi.waitFor(() => {
      expect(mocks.refreshAuthSession).toHaveBeenCalledOnce();
      expect(mocks.saveAuth).toHaveBeenCalledWith(currentAuth);
    });
  });
});
