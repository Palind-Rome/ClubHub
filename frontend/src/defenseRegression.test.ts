import { describe, expect, it } from "vitest";
import activitySource from "./views/ActivityList.vue?raw";
import budgetSource from "./views/BudgetManagement.vue?raw";
import clubSource from "./views/ClubList.vue?raw";
import evaluationSource from "./views/EvaluationList.vue?raw";
import learningSource from "./views/LearningCenter.vue?raw";
import materialSource from "./views/MaterialBorrow.vue?raw";

describe("答辩页面回归约束", () => {
  it("keeps member and evaluation actions on one compact row", () => {
    expect(clubSource).toContain("member-row-actions");
    expect(clubSource).toContain(".member-row-actions");
    expect(clubSource).toContain("flex-wrap: nowrap");
    expect(evaluationSource).toContain("evaluation-row-actions");
  });

  it("prevents organization control columns from showing ellipses", () => {
    expect(clubSource.match(/class-name="organization-control-column"/g)?.length).toBeGreaterThan(
      2,
    );
    expect(clubSource).toContain("text-overflow: clip");
    expect(clubSource).toContain("overflow: visible");
  });

  it("hides budget operation columns when the current rows have no actions", () => {
    expect(budgetSource).toContain('v-if="showAccountOperationColumn"');
    expect(budgetSource).toContain('v-if="showApplicationOperationColumn"');
    expect(budgetSource).not.toContain('label="操作" width="210" fixed="right"');
  });

  it("groups learning actions and keeps the optional end-time label on one line", () => {
    expect(learningSource).toContain("learning-row-actions");
    expect(learningSource).toContain('label="结束时间" prop="endAt"');
    expect(learningSource).not.toContain("结束时间（可选）");
  });

  it("shows saved activity and budget review comments in activity details", () => {
    expect(activitySource).toContain("活动审核意见");
    expect(activitySource).toContain("currentActivity.reviewComment");
    expect(activitySource).toContain("经费审核意见");
  });

  it("does not show return and damage actions for completed material borrows", () => {
    expect(materialSource).toContain(
      "canRecordBorrowForClub(row.clubId) && isBorrowInProgress(row)",
    );
    expect(materialSource).toContain("已完成");
  });
});
