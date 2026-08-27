import { createApp, defineComponent, nextTick, type App } from "vue";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DashboardHome from "./views/DashboardHome.vue";

const mocks = vi.hoisted(() => ({
  getActivities: vi.fn(),
  getProjects: vi.fn(),
  getNotices: vi.fn(),
  getRecruitments: vi.fn(),
  readAuth: vi.fn(),
  onSessionChange: vi.fn(),
}));

vi.mock("./apiClient", () => ({
  apiClient: {
    getActivities: mocks.getActivities,
    getProjects: mocks.getProjects,
    getNotices: mocks.getNotices,
    getRecruitments: mocks.getRecruitments,
  },
}));

vi.mock("./authSession", () => ({
  readAuth: mocks.readAuth,
  onSessionChange: mocks.onSessionChange,
}));

const studentAuth = {
  token: "student-token",
  user: { id: 7, username: "student", realName: "陈同学", accountStatus: "normal" },
  roles: [],
  permissions: ["activity:view"],
};

const presidentAuth = {
  token: "president-token",
  user: { id: 3, username: "president", realName: "王会长", accountStatus: "normal" },
  roles: [
    {
      id: 2,
      code: "CLUB_PRESIDENT",
      name: "社团负责人",
      displayName: "开源社团负责人",
      scope: "club",
      clubId: 1,
      clubIds: [1],
      permissions: ["activity:create"],
    },
  ],
  permissions: ["activity:create"],
};

let mountedApp: App<Element> | undefined;
let host: HTMLDivElement;
let sessionListener: (() => void) | undefined;
let stopSessionListener: ReturnType<typeof vi.fn>;

beforeEach(() => {
  host = document.createElement("div");
  document.body.append(host);
  stopSessionListener = vi.fn();
  sessionListener = undefined;
  mocks.readAuth.mockReturnValue(studentAuth);
  mocks.onSessionChange.mockImplementation((callback: () => void) => {
    sessionListener = callback;
    return stopSessionListener;
  });
});

afterEach(() => {
  mountedApp?.unmount();
  mountedApp = undefined;
  host.remove();
  vi.clearAllMocks();
});

describe("DashboardHome", () => {
  it("renders identity and successful real-data metrics after loading", async () => {
    resolveMetrics(2, 3, 4, 5);
    mountDashboard();

    await flushDashboard();

    expect(host.textContent).toContain("陈同学");
    expect(host.textContent).toContain("可见活动2");
    expect(host.textContent).toContain("协作项目3");
    expect(host.textContent).toContain("未读通知4");
    expect(host.textContent).toContain("纳新计划5");
    expect(host.querySelector("button")?.dataset.loading).toBe("false");
    expect(mocks.getActivities).toHaveBeenCalledWith({ currentUserId: 7 });
  });

  it("keeps empty results at zero and isolates a partial API failure", async () => {
    mocks.getActivities.mockResolvedValue([]);
    mocks.getProjects.mockRejectedValue(new Error("offline"));
    mocks.getNotices.mockResolvedValue([]);
    mocks.getRecruitments.mockResolvedValue([]);
    mountDashboard();

    await flushDashboard();

    expect(host.textContent).toContain("可见活动0");
    expect(host.textContent).toContain("协作项目—");
    expect(host.textContent).toContain("未读通知0");
    expect(host.textContent).toContain("纳新计划0");
    expect(host.textContent).toContain("暂时无法读取接口");
    expect(host.querySelector("button")?.dataset.loading).toBe("false");
  });

  it("refreshes identity and metric mapping when the session changes", async () => {
    resolveMetrics(1, 1, 1, 1);
    mountDashboard();
    await flushDashboard();

    mocks.readAuth.mockReturnValue(presidentAuth);
    resolveMetrics(6, 7, 8, 9);
    sessionListener?.();
    await flushDashboard();

    expect(host.textContent).toContain("王会长");
    expect(host.textContent).toContain("开源社团负责人");
    expect(host.textContent).toContain("可见活动6");
    expect(host.textContent).toContain("协作项目7");
    expect(host.textContent).toContain("未读通知8");
    expect(host.textContent).toContain("纳新计划9");
    expect(mocks.getActivities).toHaveBeenLastCalledWith({ currentUserId: 3 });

    mountedApp?.unmount();
    mountedApp = undefined;
    expect(stopSessionListener).toHaveBeenCalledOnce();
  });
});

function resolveMetrics(
  activityCount: number,
  projectCount: number,
  noticeCount: number,
  recruitmentCount: number,
) {
  mocks.getActivities.mockResolvedValue(Array.from({ length: activityCount }, () => ({})));
  mocks.getProjects.mockResolvedValue(Array.from({ length: projectCount }, () => ({})));
  mocks.getNotices.mockResolvedValue(Array.from({ length: noticeCount }, () => ({})));
  mocks.getRecruitments.mockResolvedValue(Array.from({ length: recruitmentCount }, () => ({})));
}

function mountDashboard() {
  mountedApp = createApp(DashboardHome);
  mountedApp.component(
    "router-link",
    defineComponent({
      props: { to: { type: String, required: true } },
      template: "<a :href='to'><slot /></a>",
    }),
  );
  mountedApp.component(
    "el-button",
    defineComponent({
      props: { loading: Boolean },
      emits: ["click"],
      template:
        "<button :data-loading='String(loading)' @click='$emit(\"click\")'><slot /></button>",
    }),
  );
  mountedApp.mount(host);
}

async function flushDashboard() {
  await Promise.resolve();
  await Promise.resolve();
  await nextTick();
}
