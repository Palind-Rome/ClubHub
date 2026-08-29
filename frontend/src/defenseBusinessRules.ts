export function activityRegistrationButtonText(options: {
  isRegistered: boolean;
  hasMemberPermission: boolean;
  canRegister: boolean;
}) {
  if (options.isRegistered) return "已报名";
  if (!options.hasMemberPermission) return "仅限本社团成员";
  return options.canRegister ? "报名" : "不可报名";
}

export function awardReviewResultText(value?: string | null) {
  const normalized = (value ?? "").trim().toLowerCase();
  const labels: Record<string, string> = {
    submit: "提交申请",
    approve: "审核通过",
    return: "退回修改",
    reject: "审核驳回",
    publish: "发布公示",
    archive: "完成归档",
    withdraw: "撤回申请",
  };
  return labels[normalized] || value || "处理";
}

export function awardApplicationEntryState(
  clubId: number | null | undefined,
  schemeCount: number,
  applicantCount: number,
) {
  if (!clubId) return { disabled: true, reason: "请先选择社团。" };
  if (schemeCount === 0) return { disabled: true, reason: "当前社团暂无在申请期内的奖项项目。" };
  if (applicantCount === 0) {
    return { disabled: true, reason: "当前身份不在有效申请成员范围内。" };
  }
  return { disabled: false, reason: "" };
}
