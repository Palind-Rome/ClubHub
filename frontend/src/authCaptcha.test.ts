import { describe, expect, it } from "vitest";
import authFlowSource from "./views/AuthFlow.vue?raw";

describe("登录验证码交互", () => {
  it("登录和注册都使用服务端验证码挑战并支持刷新", () => {
    expect(authFlowSource).toContain('"/api/v1/auth/captcha"');
    expect(authFlowSource).toContain('cache: "no-store"');
    expect(authFlowSource.match(/prop="captchaCode"/g)).toHaveLength(2);
    expect(authFlowSource).toMatch(/captchaToken:/);
    expect(authFlowSource).toContain("验证码 5 位数字，点击图片刷新");
    expect(authFlowSource).toContain('alt="验证码图片，点击刷新"');
  });
});
