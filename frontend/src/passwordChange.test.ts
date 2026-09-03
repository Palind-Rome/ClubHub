import { describe, expect, it } from "vitest";
import authFlowSource from "./views/AuthFlow.vue?raw";
import { confirmationProblem, newPasswordProblem } from "./passwordChange";

describe("修改密码", () => {
  it("校验新密码长度且不能复用当前密码", () => {
    expect(newPasswordProblem("ClubHub123", "12345")).toBe("新密码长度为 6 到 128 个字符");
    expect(newPasswordProblem("ClubHub123", "ClubHub123")).toBe("新密码不能与当前密码相同");
    expect(newPasswordProblem("ClubHub123", "NewClubHub456")).toBeNull();
  });

  it("校验两次输入的新密码一致", () => {
    expect(confirmationProblem("NewClubHub456", "")).toBe("请再次输入新密码");
    expect(confirmationProblem("NewClubHub456", "NewClubHub789")).toBe("两次输入的新密码不一致");
    expect(confirmationProblem("NewClubHub456", "NewClubHub456")).toBeNull();
  });

  it("账号页面通过生成客户端修改密码并在成功后清理会话", () => {
    expect(authFlowSource).toContain("修改密码");
    expect(authFlowSource).toContain("current-password");
    expect(authFlowSource).toContain("new-password");
    expect(authFlowSource).toContain("apiClient.changeCurrentUserPassword");
    expect(authFlowSource).toContain("clearSession()");
    expect(authFlowSource).toContain('router.replace("/auth")');
  });
});
