# ClubHub

本仓库用于同济大学 2026 年小学期《数据库课程设计》项目：ClubHub 高校社团运营与协同管理平台。

## 先阅读

- `README.md`：项目目标、技术栈和当前状态。
- `CONTRIBUTING.md`：组员协作、分支、PR、CI/CD、安全规范。
- `database/README.md`：Oracle 初始化、验证和数据库变更规则。
- `docs/数据库设计文档.doc`：已经完成的设计文档。

## 课程要求

全部要求在 `docs/2026《数据库课程设计》课程提纲.doc` 中，Agent 没有必要直接阅读，大致总结如下：

- 使用较新版本 Visual Studio / VS.NET。
- 使用 C#。
- 使用 Oracle Database 18c 或更高版本作为 DBMS。
- 使用 Oracle 数据访问组件或 ORM 框架。
- C/S 或 B/S 均可。本项目采用 B/S。
- 至少 12 张表，且符合第三范式。
- 至少 20 个功能点，其中至少 15 个必须有业务逻辑，不能只是单表增删改查。
- 最终完成编码、测试、部署或可执行程序，以及系统需求分析文档、数据库设计文档、系统设计与实现文档、答辩 PPT。
- GitHub commit、PR、Issue 可以作为贡献和考勤依据，提交记录必须清晰。

## 架构

前后端分离，实现轻量：

- `backend/`：ASP.NET Core Web API，负责 C# 业务逻辑、权限、Oracle 数据访问。
- `frontend/`：Vue 3 + Vite + Element Plus，负责页面和交互。
- `database/`：Oracle 建表脚本、验证脚本、种子数据、视图和迁移脚本。
- `docs/`：各类文档。
- `docker-compose.yml`：生产环境 Docker 编排。
- `docker-compose.dev.yml`：本地开发环境（源码挂载 + 热重载，只需 Docker）。

## 数据库基线

当前数据库设计共有 24 张 Oracle 表：

- USERS、ROLES、USER_ROLES
- CLUBS、CLUB_MEMBERS、RECRUITMENTS、RECRUITMENT_APPLICATIONS
- ACTIVITIES、ACTIVITY_PARTICIPATIONS、VENUES、VENUE_RESERVATIONS
- PROJECTS、PROJECT_MEMBERS、PROJECT_TASKS、PROJECT_TASK_ASSIGNEES
- LEARNING_ITEMS、LEARNING_RECORDS
- MATERIALS、MATERIAL_BORROWS、EVALUATIONS
- NOTICES、NOTICE_READS、FORUM_POSTS、OPERATION_LOGS

数据库脚本放在 `database/schema.sql`。不要修改表结构。如果确有必要，请先与用户讨论。

CI 不负责自动刷新生产/远程数据库，不负责自动重建索引。全量刷新只允许用于本地开发库或明确的测试库；生产/演示库的结构变更必须通过人工确认后的迁移脚本执行。

## 开发规范

详细开发流程、分支策略、Commit 格式、API-first 工作流、CI/CD 说明、命令速查等
全部内容请阅读 **`CONTRIBUTING.md`**，本文档不再重复。

### 快速索引

| 你需要了解 | 看这里 |
|-----------|--------|
| 分支怎么分、怎么开 | `CONTRIBUTING.md` → 分支要求 |
| 一个功能点从写到合的全流程 | `CONTRIBUTING.md` → 日常开发流程 |
| PR 门禁跑什么、怎么通过 | `CONTRIBUTING.md` → CI/CD 策略 → PR 门禁 |
| dev 怎么上线到 main | `CONTRIBUTING.md` → CI/CD 策略 → 阶段性上线 |
| Commit 怎么写 | `CONTRIBUTING.md` → Commit 信息 |
| 代码分区（什么能改什么不能改） | `CONTRIBUTING.md` → API-first 开发流程 |
| 本地怎么跑、常用命令 | `CONTRIBUTING.md` → 本地运行 + 常用命令速查 |

## Agent 操作约束

以下约束**仅针对 Agent**（Claude Code 等），人类组员不受限制。

### 核心流程

- **Issue 先行**：开始任何实质性改动前，先确认是否存在对应 Issue——没有则创建并获得用户确认，再继续后续操作。不允许在未创建 Issue 的情况下直接实施改动并开 PR。此规则适用于所有改动来源（用户指令或自主发现），仅阶段性合入主线（dev → main）、CI 自动化提交可跳过。
- **用户确认**：Agent **禁止**在未经用户确认的情况下创建 Issue、执行 `git commit`、`git push` 或进行其他对外操作。**绝对禁止对 dev 和 main 分支使用 `push -f`**，非必要情况下也不准使用。
- **修改后同步文档**：倘若 Agent 修改 CI/CD 工作流、项目结构、分支策略、开发流程后，**必须同步更新对应的文档**（`CONTRIBUTING.md`、`AGENTS.md`、`README.md`），保持代码和文档一致。
- **CodeRabbit review**：PR 标记 Ready 后，CodeRabbit 会自动 review。Agent 必须阅读 review 意见：合理的直接采纳并 push，不合理的在 PR 评论中回复说明原因。

### API 开发

- **API-first**：修改 API 必须先改 `api/openapi.yaml`，禁止绕过契约在 Controller 里硬编码请求/响应模型。
- **禁止手改生成代码**：`frontend/src/api/*` 和 `backend/Models/*` 是 CI 自动生成的，手改会被覆盖。

### 分支与 PR

- **分支命名**：`类型/编号-简短描述`，例如 `feature/42-activity-checkin`、`fix/56-venue-conflict`、`docs/55-pr-title-format`，编号代表 issue 编号，允许的类型前缀见 `CONTRIBUTING.md` → 分支要求。
- **立即开 draft PR**：创建分支后先做至少一个初始 commit 并 push，再执行 `gh pr create --draft`（`gh` 要求分支至少有一个领先 commit），代码完成后标记 Ready for Review。
- **PR 标题**：必须使用 Conventional Commits 格式，例如 `feat(activity): 新增活动报名人数限制`。允许的 type 见 `CONTRIBUTING.md` → Commit 信息。
- **标签必选**：创建 Issue 或 PR 时必须同时标注类型、优先级、领域三个标签。创建前先运行 `gh label list` 获取当前所有可用标签。创建命令应附带 `--label` 参数一次性标注。
- **Issue 模板**：`gh issue create` 使用 `--template` 指定正确的模板文件。
- **PR 模板**：位于 `.github/pull_request_template.md`，创建 PR 前应读取并按章节逐项填写。
- **同步 PR 描述**：每次推送新 commit 后，检查 PR 描述是否与实际改动一致。如有新增或变更，用 `gh pr edit` 更新 PR 描述的"改动内容"和"关联 Issue"等字段，确保合并前描述始终反映最新状态。
- **功能分支同步 dev**：功能分支落后于 `dev` 需要同步时，**禁止使用 `git merge`**，一律采用以下流程：
  - 确保当前功能分支已推送至远程（至少有一个 commit 领先）。
  - 执行 `gh pr update-branch --rebase`，让 GitHub 在服务端完成 rebase（等效于网页端 PR 页面点击 "Update branch" → 选择 "Update with rebase"）。
  - 如果有冲突，GitHub 会报告冲突。**立即停止**，向用户报告所有冲突文件及大致内容，等待用户指示。用户可选择自行解冲突，或指示 Agent 在本地解冲突后 `git push --force-with-lease`。
  - 冲突解决后（或没有冲突时），本地执行 `git pull --rebase` 同步最新分支。
  - 不要在本地执行 `git merge dev` 或手动 `git rebase + force push`。如果发现当前分支已有合并提交（即未遵从本规范），应通过交互式 rebase（`git rebase -i origin/dev`，将合并提交标记为 drop 或 squash）移除多余的合并提交，再 `git push --force-with-lease` 推送。





## 提交前

- 本次修改能对应到一个功能点、文档任务、数据库任务或基础设施任务。
- 数据库若改动，已同步脚本、迁移说明和文档。
- 新增业务逻辑有测试或手工验证说明。
- 工作已 Issue/PR/commit。
