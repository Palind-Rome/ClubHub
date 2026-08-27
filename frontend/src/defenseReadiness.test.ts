import { describe, expect, it } from "vitest";
import appShellSource from "./components/shell/AppShell.vue?raw";
import dashboardSource from "./views/DashboardHome.vue?raw";
import routerSource from "./router/index.ts?raw";

const viewSources = import.meta.glob("./views/*.vue", {
  eager: true,
  import: "default",
  query: "?raw",
}) as Record<string, string>;

const defenseTableViews = [
  "RecruitmentList.vue",
  "EvaluationList.vue",
  "ActivityList.vue",
  "NoticeCenter.vue",
  "ProjectList.vue",
  "LearningCenter.vue",
];

describe("答辩演示就绪约束", () => {
  it("默认进入答辩工作台且统计来自真实接口", () => {
    expect(routerSource).toContain('path: "/dashboard"');
    expect(routerSource).toContain('redirect: "/dashboard"');
    expect(dashboardSource).toContain("Promise.allSettled");
    expect(dashboardSource).toContain("apiClient.getActivities");
    expect(dashboardSource).toContain("apiClient.getProjects");
    expect(dashboardSource).toContain("apiClient.getNotices");
    expect(dashboardSource).toContain("apiClient.getRecruitments");
    expect(dashboardSource).toContain('metric.value ?? "—"');
    expect(dashboardSource).toContain("演示待办");
    expect(dashboardSource).toContain("当前身份没有有效社团范围角色");
    expect(dashboardSource).toContain("核心业务入口");
  });

  it("页面眉标由路由语义提供，不再硬编码通用 Workspace", () => {
    expect(appShellSource).toContain("{{ pageEyebrow }}");
    expect(appShellSource).not.toContain("ClubHub Workspace");
    expect(routerSource).toContain('eyebrow: "Activity Operations"');
    expect(routerSource).toContain('eyebrow: "Learning Center"');
  });

  it("后端状态灯在文字左侧保留明确间距", () => {
    expect(appShellSource).toContain(".health-button :deep(> span)");
    expect(appShellSource).toContain("gap: 10px");
  });

  it.each(defenseTableViews)("%s 的主数据表使用答辩表格表面", (fileName) => {
    expect(viewSources[`./views/${fileName}`]).toContain("defense-data-table");
  });

  it("核心列表不再使用遮挡正文的超宽固定操作列", () => {
    const sources = defenseTableViews.map((name) => viewSources[`./views/${name}`]).join("\n");
    expect(sources).not.toContain('width="560" fixed="right"');
    expect(sources).not.toContain('width="420" align="center" fixed="right"');
    expect(sources).not.toContain('width="340" fixed="right"');
  });

  it("无适用业务范围时，项目、奖项和讨论发布入口会预先解释", () => {
    expect(viewSources["./views/ProjectList.vue"]).toContain("creatableClubs.length === 0");
    expect(viewSources["./views/AwardList.vue"]).toContain("applicantOptions.length === 0");
    expect(viewSources["./views/ForumCenter.vue"]).toContain("canPostToSelectedClub");
  });
});
