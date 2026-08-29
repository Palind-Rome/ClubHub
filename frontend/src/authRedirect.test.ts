import { describe, expect, it } from "vitest";
import { authRedirectPath } from "./authRedirect";

describe("authRedirectPath", () => {
  it("keeps a valid internal absolute path", () => {
    expect(authRedirectPath("/projects")).toBe("/projects");
  });

  it("falls back to the dashboard when redirect is missing", () => {
    expect(authRedirectPath(undefined)).toBe("/dashboard");
  });

  it("falls back to the dashboard for non-string and protocol-relative values", () => {
    expect(authRedirectPath(["/projects"])).toBe("/dashboard");
    expect(authRedirectPath("//example.com")).toBe("/dashboard");
  });
});
