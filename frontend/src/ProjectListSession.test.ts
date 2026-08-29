import { createApp, defineComponent, nextTick, type App, type ComponentPublicInstance } from "vue";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import ProjectList from "./views/ProjectList.vue";

const mocks = vi.hoisted(() => ({
  getClubs: vi.fn(),
  getProjects: vi.fn(),
  getClubMembers: vi.fn(),
  readAuth: vi.fn(),
  onSessionChange: vi.fn(),
}));

vi.mock("./api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./api")>();
  return {
    ...actual,
    DefaultApi: class {
      getClubs = mocks.getClubs;
    },
  };
});

vi.mock("./apiClient", () => ({
  apiClient: {
    getProjects: mocks.getProjects,
    getClubMembers: mocks.getClubMembers,
  },
}));

vi.mock("./authSession", () => ({
  readAuth: mocks.readAuth,
  onSessionChange: mocks.onSessionChange,
}));

type ProjectListExposed = ComponentPublicInstance & {
  projects: Array<{ id: number; projectName: string }>;
};

let app: App<Element> | undefined;
let host: HTMLDivElement;
let sessionListener: (() => void) | undefined;

beforeEach(() => {
  host = document.createElement("div");
  document.body.append(host);
  mocks.getClubs.mockResolvedValue([]);
  mocks.getClubMembers.mockResolvedValue([]);
  mocks.readAuth.mockReturnValue({
    token: "old-token",
    user: { id: 1, username: "old", realName: "旧账号", accountStatus: "active" },
    roles: [],
    permissions: [],
  });
  mocks.onSessionChange.mockImplementation((callback: () => void) => {
    sessionListener = callback;
    return vi.fn();
  });
});

afterEach(() => {
  app?.unmount();
  app = undefined;
  host.remove();
  vi.clearAllMocks();
});

describe("ProjectList session isolation", () => {
  it("does not let an earlier session request overwrite the new session", async () => {
    const oldProjects = deferred<Array<{ id: number; clubId: number; projectName: string }>>();
    mocks.getProjects
      .mockReturnValueOnce(oldProjects.promise)
      .mockResolvedValueOnce([{ id: 2, clubId: 1, projectName: "新会话项目" }]);

    app = createApp(ProjectList);
    app.component("el-table-column", defineComponent({ template: "<div />" }));
    const component = app.mount(host) as ProjectListExposed;
    await flushPromises();
    expect(mocks.getProjects).toHaveBeenCalledOnce();

    mocks.readAuth.mockReturnValue({
      token: "new-token",
      user: { id: 2, username: "new", realName: "新账号", accountStatus: "active" },
      roles: [],
      permissions: [],
    });
    sessionListener?.();
    await flushPromises();

    oldProjects.resolve([{ id: 1, clubId: 1, projectName: "旧会话项目" }]);
    await flushPromises();

    expect(component.projects).toHaveLength(1);
    expect(component.projects[0]?.projectName).toBe("新会话项目");
  });
});

async function flushPromises() {
  await Promise.resolve();
  await Promise.resolve();
  await nextTick();
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}
