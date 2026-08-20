/// <reference types="node" />

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import appNavigationSource from "./components/shell/AppNavigation.vue?raw";
import appShellSource from "./components/shell/AppShell.vue?raw";
import appPanelSource from "./components/ui/AppPanel.vue?raw";

const globalStyleSource = readFileSync(resolve(process.cwd(), "src/style.css"), "utf8");

const viewSources = import.meta.glob("./views/*.vue", {
  eager: true,
  import: "default",
  query: "?raw",
}) as Record<string, string>;

const darkModeCriticalViews = [
  "AuthFlow.vue",
  "AwardList.vue",
  "ClubList.vue",
  "EvaluationList.vue",
  "MaterialBorrow.vue",
  "NoticeCenter.vue",
  "VenueReservationApply.vue",
];

const standardizedHeaderViews = [
  "ActivityList.vue",
  "AuthFlow.vue",
  "AwardList.vue",
  "BudgetManagement.vue",
  "ClubList.vue",
  "DashboardHome.vue",
  "EvaluationList.vue",
  "ForumCenter.vue",
  "LearningCenter.vue",
  "MaterialBorrow.vue",
  "NoticeCenter.vue",
  "ProjectList.vue",
  "ProjectWorkspace.vue",
  "RecruitmentList.vue",
  "VenueManage.vue",
  "VenueReservationApply.vue",
];

describe("设计系统样式约束", () => {
  it.each(darkModeCriticalViews)("%s 不再强制使用浅色表面", (fileName) => {
    const source = viewSources[`./views/${fileName}`];

    expect(source).toBeDefined();
    expect(source).not.toMatch(/background(?:-color)?:\s*(?:#fff(?:fff)?|white);/i);
  });

  it.each(["AwardList.vue", "ClubList.vue", "NoticeCenter.vue"])(
    "%s 使用主题文本颜色",
    (fileName) => {
      const source = viewSources[`./views/${fileName}`];

      expect(source).toBeDefined();
      expect(source).not.toMatch(/color:\s*#(?:1f2d3d|20262e|374151|66727f|6b7280);/i);
    },
  );

  it.each(standardizedHeaderViews)("%s 使用统一页面标题规范", (fileName) => {
    expect(viewSources[`./views/${fileName}`]).toContain("app-page-header");
  });

  it("由应用壳层统一页面宽度、居中和全屏留白", () => {
    expect(appShellSource).toContain("max-width: var(--club-content-width)");
    expect(appShellSource).toContain("padding: clamp(var(--club-space-5), 2.2vw, 40px)");
    expect(appShellSource).toContain(":global(.page-content > *)");
    expect(appShellSource).toContain("max-width: 100% !important");
    expect(appShellSource).toContain("margin-inline: auto !important");
  });

  it("应用壳层使用透明背景品牌图标", () => {
    expect(appShellSource.match(/src="\/favicon\.svg"/g)).toHaveLength(2);
    expect(appShellSource).toContain("background: transparent");
    expect(appShellSource).toContain("object-fit: contain");
  });

  it("账号入口提供三态主题切换并精简介绍文案", () => {
    const authSource = viewSources["./views/AuthFlow.vue"];

    expect(authSource).toContain('class="public-theme-switch"');
    expect(authSource).toContain('command="system"');
    expect(authSource).toContain('command="light"');
    expect(authSource).toContain('command="dark"');
    expect(authSource).not.toContain("按角色呈现专属工作入口");
    expect(authSource).not.toContain("浅色、深色与系统主题自由切换");
    expect(authSource).not.toContain("重要通知和近期任务集中查看");
  });

  it("结构性选中态与身份条只使用蓝色系", () => {
    const dashboardSource = viewSources["./views/DashboardHome.vue"];

    expect(appNavigationSource).not.toContain("var(--club-accent-soft)");
    expect(dashboardSource).toContain("color-mix(in srgb, var(--club-primary) 6%, transparent)");
    expect(dashboardSource).toContain(
      "linear-gradient(135deg, var(--club-primary), var(--club-primary-strong))",
    );
  });

  it("全局使用纯色背景和更清晰的毛玻璃层次", () => {
    expect(globalStyleSource).toContain("--club-bg: #f4f5f7");
    expect(globalStyleSource).toContain("--club-glass-blur: blur(24px) saturate(120%)");
    expect(globalStyleSource).not.toContain("radial-gradient");
    expect(appShellSource).toContain("-webkit-backdrop-filter: var(--club-glass-blur)");
    expect(appPanelSource).toContain("-webkit-backdrop-filter: var(--club-glass-blur)");
  });
});
