import { createApp, type Component } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import AppEmptyState from "./AppEmptyState.vue";
import AppErrorState from "./AppErrorState.vue";
import AppLoadingState from "./AppLoadingState.vue";
import AppPageHeader from "./AppPageHeader.vue";

const mountedApps: ReturnType<typeof createApp>[] = [];

function mount(component: Component, props: Record<string, unknown> = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const app = createApp(component, props);
  app.config.warnHandler = () => undefined;
  app.mount(host);
  mountedApps.push(app);
  return host;
}

afterEach(() => {
  mountedApps.splice(0).forEach((app) => app.unmount());
  document.body.replaceChildren();
});

describe("公共页面组件", () => {
  it("页面标题呈现标题、描述和眉题", () => {
    const host = mount(AppPageHeader, {
      eyebrow: "社团运营",
      title: "成员管理",
      description: "维护社团成员与任期信息。",
    });

    expect(host.textContent).toContain("社团运营");
    expect(host.querySelector("h2")?.textContent).toBe("成员管理");
    expect(host.textContent).toContain("维护社团成员与任期信息。");
  });

  it("加载状态提供可访问的状态提示", () => {
    const host = mount(AppLoadingState, { title: "正在加载活动" });
    const status = host.querySelector('[role="status"]');

    expect(status?.getAttribute("aria-label")).toBe("正在加载活动");
    expect(status?.textContent).toContain("正在加载活动");
  });

  it("空状态支持业务自定义文案", () => {
    const host = mount(AppEmptyState, {
      title: "暂无通知",
      description: "已读完全部通知。",
    });

    expect(host.textContent).toContain("暂无通知");
    expect(host.textContent).toContain("已读完全部通知。");
  });

  it("错误状态提供告警语义和重试事件", async () => {
    const onRetry = vi.fn();
    const host = mount(AppErrorState, { title: "请求失败", onRetry });

    expect(host.querySelector('[role="alert"]')).not.toBeNull();
    (host.querySelector("el-button") as HTMLElement).click();
    await Promise.resolve();
    expect(onRetry).toHaveBeenCalledOnce();
  });
});
