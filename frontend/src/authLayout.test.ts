import { describe, expect, it } from "vitest";
import authFlowSource from "./views/AuthFlow.vue?raw";

describe("登录注册页面布局", () => {
  it("使用完整视口高度垂直居中认证内容", () => {
    const authShellRule = authFlowSource.match(/\.auth-shell\s*\{([\s\S]*?)\}/)?.[1];

    expect(authShellRule).toBeDefined();
    expect(authShellRule).toContain("min-height: 100vh");
    expect(authShellRule).toContain("align-items: center");
    expect(authShellRule).toContain("padding-block: 24px");
    expect(authShellRule).not.toContain("padding-top");
  });
});
