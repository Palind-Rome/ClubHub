import { execFileSync } from "node:child_process";
import { resolve } from "node:path";
import { describe, it } from "vitest";

const dockerBuildTest = process.env.GITHUB_ACTIONS === "true" ? it : it.skip;

describe("生产前端 Docker 构建", () => {
  dockerBuildTest(
    "使用部署 Dockerfile 可以完成镜像构建",
    () => {
      const repoRoot = resolve(process.cwd(), "..");

      execFileSync(
        "docker",
        ["build", "--file", "frontend/Dockerfile", "--tag", "clubhub-frontend:ci", "."],
        {
          cwd: repoRoot,
          stdio: "inherit",
          timeout: 300_000,
        },
      );
    },
    300_000,
  );
});
