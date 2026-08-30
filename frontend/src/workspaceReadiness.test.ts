import { describe, expect, it } from "vitest";
import appShellSource from "./components/shell/AppShell.vue?raw";
import routerSource from "./router/index.ts?raw";

const viewSources = import.meta.glob("./views/*.vue", {
  eager: true,
  import: "default",
  query: "?raw",
}) as Record<string, string>;

const businessTableViews = [
  "RecruitmentList.vue",
  "EvaluationList.vue",
  "ActivityList.vue",
  "NoticeCenter.vue",
  "ProjectList.vue",
  "LearningCenter.vue",
];

describe("运营工作台就绪约束", () => {
  it("默认进入运营工作台", () => {
    expect(routerSource).toContain('path: "/dashboard"');
    expect(routerSource).toContain('redirect: "/dashboard"');
    expect(routerSource).toContain('title: "运营工作台"');
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

  it.each(businessTableViews)("%s 的主数据表使用统一业务表格表面", (fileName) => {
    const source = viewSources[`./views/${fileName}`];
    expect(source).toMatch(/<el-table\b[^>]*\bbusiness-data-table\b[^>]*>/);
  });

  it.each(businessTableViews)("%s 的主表不使用固定右侧列", (fileName) => {
    const source = viewSources[`./views/${fileName}`];
    const classOffset = source.indexOf("business-data-table");
    const tableStart = source.lastIndexOf("<el-table", classOffset);
    const tableEnd = source.indexOf("</el-table>", classOffset);

    expect(classOffset).toBeGreaterThan(-1);
    expect(tableStart).toBeGreaterThan(-1);
    expect(tableEnd).toBeGreaterThan(tableStart);
    expect(source.slice(tableStart, tableEnd)).not.toContain('fixed="right"');
  });

  it("无适用业务范围时，项目、奖项和讨论发布入口会预先解释", () => {
    const projectSource = viewSources["./views/ProjectList.vue"];
    const awardSource = viewSources["./views/AwardList.vue"];
    const forumSource = viewSources["./views/ForumCenter.vue"];
    expect(projectSource).toContain(':disabled="!currentUserId || creatableClubs.length === 0"');
    expect(projectSource).toContain("当前账号没有可提交立项申请的社团");
    expect(awardSource).toContain(':disabled="applicationEntry.disabled"');
    expect(awardSource).toContain(':content="applicationEntry.reason"');
    expect(forumSource).toContain('<el-card v-if="canPostToSelectedClub"');
    expect(forumSource).toContain("只有当前具有发布权限的成员才能发布或回复");
  });

  it("答辩写操作不依赖旧浏览器的 crypto.randomUUID", () => {
    for (const fileName of [
      "ProjectList.vue",
      "LearningCenter.vue",
      "ActivityList.vue",
      "BudgetManagement.vue",
    ]) {
      expect(viewSources[`./views/${fileName}`]).not.toContain("crypto.randomUUID");
    }
  });

  it("活动审核入口在待审核行直接展示", () => {
    const activitySource = viewSources["./views/ActivityList.vue"];
    expect(activitySource).toContain('v-if="canReviewActivity(row)"');
    expect(activitySource).toContain("活动审核");
    expect(activitySource).toContain("apiClient.reviewActivity");
  });

  it("经费审核使用带幂等键的 API 客户端", () => {
    const budgetSource = viewSources["./views/BudgetManagement.vue"];
    expect(budgetSource).toContain("apiClient.reviewBudgetApplication");
    expect(budgetSource).toContain("createIdempotencyKey()");
  });

  it("业务时间展示统一经过北京时间格式化", () => {
    expect(viewSources["./views/AwardList.vue"]).toContain("formatBeijingDateTime");
    expect(viewSources["./views/NoticeCenter.vue"]).toContain("formatBeijingDateTime");
    expect(viewSources["./views/ForumCenter.vue"]).toContain("formatBeijingDateTime");
    expect(viewSources["./views/EvaluationList.vue"]).toContain("formatBeijingDateTime");
  });

  it("考核编辑会保留当前表单并按数字提交四项分数", () => {
    const evaluationSource = viewSources["./views/EvaluationList.vue"];
    expect(evaluationSource).toContain("invalidateScorePreview");
    expect(evaluationSource).toContain(
      "learningScore: normalizeScore(evaluationForm.learningScore)",
    );
  });
});
