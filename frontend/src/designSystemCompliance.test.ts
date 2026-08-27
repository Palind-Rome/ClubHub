import { describe, expect, it } from "vitest";
import appNavigationSource from "./components/shell/AppNavigation.vue?raw";
import appShellSource from "./components/shell/AppShell.vue?raw";

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

  it("激活菜单使用蓝粉背景且不叠加内嵌阴影", () => {
    const activeRule = appNavigationSource.match(
      /\.navigation-menu :deep\(\.el-menu-item\.is-active\) \{([\s\S]*?)\}/,
    )?.[1];

    expect(activeRule).toContain("var(--club-primary-soft)");
    expect(activeRule).toContain("var(--club-accent-soft)");
    expect(activeRule).not.toContain("box-shadow");
  });
});
