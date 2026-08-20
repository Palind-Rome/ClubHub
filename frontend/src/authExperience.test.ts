import { describe, expect, it } from "vitest";
import { resolveIdentityLabel, resolvePostAuthPath } from "./authExperience";

describe("账号入口体验", () => {
  it("按学工号长度识别学生和教师", () => {
    expect(resolveIdentityLabel("2450001")).toBe("学生");
    expect(resolveIdentityLabel("10001")).toBe("教师");
    expect(resolveIdentityLabel("abc")).toBe("");
  });

  it("登录后默认进入工作台", () => {
    expect(resolvePostAuthPath(undefined)).toBe("/dashboard");
    expect(resolvePostAuthPath("/auth")).toBe("/dashboard");
  });

  it("仅接受站内绝对路径作为回跳目标", () => {
    expect(resolvePostAuthPath("/projects/8/workspace")).toBe("/projects/8/workspace");
    expect(resolvePostAuthPath("//example.com/path")).toBe("/dashboard");
    expect(resolvePostAuthPath("https://example.com")).toBe("/dashboard");
  });
});
