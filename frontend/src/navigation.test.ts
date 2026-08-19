import { describe, expect, it } from "vitest";
import { buildNavigationGroups, resolveActiveNavigation } from "./navigation";

function visiblePaths(permissions: string[]) {
  return buildNavigationGroups(permissions).flatMap((group) =>
    group.items.map((item) => item.path),
  );
}

describe("navigation", () => {
  it("普通用户只看到无权限门槛的入口", () => {
    const paths = visiblePaths([]);

    expect(paths).toContain("/clubs");
    expect(paths).toContain("/learning");
    expect(paths).not.toContain("/club-registration");
    expect(paths).not.toContain("/budgets");
    expect(paths).not.toContain("/venues");
    expect(paths).not.toContain("/materials");
  });

  it("按既有权限显示受限入口", () => {
    const paths = visiblePaths([
      "club:review",
      "budget:view",
      "venue:reserve",
      "material:borrow:use",
    ]);

    expect(paths).toContain("/club-registration");
    expect(paths).toContain("/budgets");
    expect(paths).toContain("/venue-reservations");
    expect(paths).toContain("/materials");
    expect(paths).not.toContain("/venues");
  });

  it("通配权限显示所有入口", () => {
    const paths = visiblePaths(["*"]);

    expect(paths).toEqual(
      expect.arrayContaining([
        "/club-registration",
        "/budgets",
        "/venues",
        "/venue-reservations",
        "/materials",
      ]),
    );
  });

  it("详情页保持所属主导航高亮", () => {
    expect(resolveActiveNavigation("/projects/42/workspace")).toBe("/projects");
    expect(resolveActiveNavigation("/recruitments/3/applications")).toBe("/recruitments");
    expect(resolveActiveNavigation("/activities")).toBe("/activities");
  });
});
