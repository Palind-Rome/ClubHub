import { describe, expect, it } from "vitest";
import clubListSource from "./views/ClubList.vue?raw";
import {
  activityRegistrationButtonText,
  awardApplicationEntryState,
  awardReviewResultText,
} from "./defenseBusinessRules";

describe("答辩业务入口规则", () => {
  it("explains that activity registration is limited to members with club scope", () => {
    expect(
      activityRegistrationButtonText({
        isRegistered: false,
        hasMemberPermission: false,
        canRegister: false,
      }),
    ).toBe("仅限本社团成员");
    expect(
      activityRegistrationButtonText({
        isRegistered: false,
        hasMemberPermission: true,
        canRegister: true,
      }),
    ).toBe("报名");
  });

  it.each([
    ["submit", "提交申请"],
    ["approve", "审核通过"],
    ["return", "退回修改"],
    ["reject", "审核驳回"],
    ["publish", "发布公示"],
    ["archive", "完成归档"],
    ["withdraw", "撤回申请"],
  ])("maps award review result %s", (value, label) => {
    expect(awardReviewResultText(value)).toBe(label);
  });

  it("disables award applications for each missing prerequisite", () => {
    expect(awardApplicationEntryState(null, 1, 1)).toEqual({
      disabled: true,
      reason: "请先选择社团。",
    });
    expect(awardApplicationEntryState(1, 0, 1)).toEqual({
      disabled: true,
      reason: "当前社团暂无在申请期内的奖项项目。",
    });
    expect(awardApplicationEntryState(1, 1, 0)).toEqual({
      disabled: true,
      reason: "当前身份不在有效申请成员范围内。",
    });
    expect(awardApplicationEntryState(1, 1, 1).disabled).toBe(false);
  });

  it("uses the same material wording in club application validation, form, and details", () => {
    expect(clubListSource).toContain('message: "请填写材料链接或归档编号"');
    expect(clubListSource.match(/label="材料链接或归档编号"/g)).toHaveLength(4);
  });
});
