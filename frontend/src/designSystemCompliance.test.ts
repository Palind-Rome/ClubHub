import { describe, expect, it } from "vitest";

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
});
