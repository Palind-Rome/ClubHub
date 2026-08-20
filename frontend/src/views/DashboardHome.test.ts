import ElementPlus from "element-plus";
import { createApp, defineComponent, h, type App as VueApp } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DashboardHome from "./DashboardHome.vue";

const apiMocks = vi.hoisted(() => ({
  getActivities: vi.fn(),
  getNotices: vi.fn(),
  getProjects: vi.fn(),
}));

vi.mock("../apiClient", () => ({ apiClient: apiMocks }));

vi.mock("../authSession", () => ({
  onSessionChange: vi.fn(() => () => undefined),
  readAuth: vi.fn(() => ({
    token: "test-token",
    user: {
      id: 24,
      username: "student",
      realName: "测试学生",
      studentNo: "2450001",
      college: "计算机科学与技术学院",
      accountStatus: "normal",
    },
    roles: [
      {
        id: 1,
        code: "student",
        name: "普通学生",
        displayName: "普通学生",
        scope: "system",
        clubIds: [],
        permissions: [],
      },
    ],
    permissions: [],
  })),
}));

const RouteStub = defineComponent({ setup: () => () => h("div") });
let mountedApp: VueApp<Element> | null = null;

beforeEach(() => {
  apiMocks.getActivities.mockReset();
  apiMocks.getNotices.mockReset();
  apiMocks.getProjects.mockReset();
});

afterEach(() => {
  mountedApp?.unmount();
  mountedApp = null;
  document.body.replaceChildren();
});

async function mountDashboard() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/dashboard", component: DashboardHome },
      { path: "/notices", component: RouteStub },
      { path: "/activities", component: RouteStub },
      { path: "/projects", component: RouteStub },
      { path: "/projects/:projectId/workspace", component: RouteStub },
      { path: "/clubs", component: RouteStub },
      { path: "/club-organization", component: RouteStub },
      { path: "/club-members", component: RouteStub },
      { path: "/recruitments", component: RouteStub },
      { path: "/evaluations", component: RouteStub },
      { path: "/awards", component: RouteStub },
      { path: "/forum", component: RouteStub },
      { path: "/learning", component: RouteStub },
    ],
  });
  await router.push("/dashboard");
  await router.isReady();

  const host = document.createElement("div");
  document.body.appendChild(host);
  mountedApp = createApp(DashboardHome);
  mountedApp.use(ElementPlus);
  mountedApp.use(router);
  mountedApp.mount(host);
  return host;
}

function activity() {
  return {
    id: 8,
    title: "迎新活动",
    clubName: "计算机协会",
    clubId: 1,
    startTime: new Date("2099-08-28T08:00:00Z"),
    endTime: new Date("2099-08-28T10:00:00Z"),
    status: "published",
    currentParticipants: 12,
    isRegistered: false,
  };
}

function project() {
  return {
    id: 9,
    clubId: 1,
    projectName: "创新项目",
    startDate: new Date("2026-08-01T00:00:00Z"),
    projectStatus: "running",
    canViewTasks: true,
    createdAt: new Date("2026-08-10T00:00:00Z"),
  };
}

function notice() {
  return {
    id: 10,
    publisherUserId: 1,
    noticeType: "announcement",
    title: "选课提醒",
    content: "请按时选课",
    targetType: "school",
    publishAt: new Date("2026-08-20T08:00:00Z"),
    noticeStatus: "published",
    isRead: false,
  };
}

describe("个性化工作台", () => {
  it("单个接口失败时仍展示其他区块数据", async () => {
    apiMocks.getNotices.mockRejectedValue(new Error("通知服务暂不可用"));
    apiMocks.getActivities.mockResolvedValue([activity()]);
    apiMocks.getProjects.mockResolvedValue([project()]);

    const host = await mountDashboard();

    await vi.waitFor(() => {
      expect(host.textContent).toContain("通知服务暂不可用");
      expect(host.textContent).toContain("迎新活动");
      expect(host.textContent).toContain("创新项目");
    });
  });

  it("失败区块可以独立重试", async () => {
    apiMocks.getNotices
      .mockRejectedValueOnce(new Error("通知加载超时"))
      .mockResolvedValueOnce([notice()]);
    apiMocks.getActivities.mockResolvedValue([]);
    apiMocks.getProjects.mockResolvedValue([]);

    const host = await mountDashboard();

    await vi.waitFor(() => expect(host.textContent).toContain("通知加载超时"));
    const retryButton = host.querySelector('[data-section="notices"] button');
    expect(retryButton).not.toBeNull();
    (retryButton as HTMLButtonElement).click();

    await vi.waitFor(() => expect(host.textContent).toContain("选课提醒"));
    expect(apiMocks.getNotices).toHaveBeenCalledTimes(2);
    expect(apiMocks.getActivities).toHaveBeenCalledOnce();
    expect(apiMocks.getProjects).toHaveBeenCalledOnce();
  });
});
