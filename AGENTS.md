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

当前数据库设计共有 22 张 Oracle 表：

- USERS、ROLES、USER_ROLES
- CLUBS、CLUB_MEMBERS、RECRUITMENTS、RECRUITMENT_APPLICATIONS
- ACTIVITIES、ACTIVITY_PARTICIPATIONS、VENUES、VENUE_RESERVATIONS
- PROJECTS、PROJECT_TASKS
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

### Agent 特别提醒

- 修改 API 必须先改 `api/openapi.yaml`，禁止绕过契约在 Controller 里硬编码请求/响应模型。
- `frontend/src/api/*` 和 `backend/Models/*` 是 CI 自动生成的，禁止手改。
- 新分支应立即用 `gh pr create --draft` 创建 draft PR。
- **Issue 驱动**：PR 应尽可能关联一个 Issue。创建分支前按以下标准判断：
  - **必须关联 Issue**：修复缺陷、实现课程功能点、功能改进、文档编写、数据库变更等所有"解决某个具体问题"的 PR
  - **可以不关联 Issue**：阶段性合入主线（dev → main）、CI 自动化流程的自动提交（如 gen-api-code.yml 生成的代码回推）等不针对特定业务问题的 PR
- PR 标题必须使用 Conventional Commits 格式（与 Commit 信息一致），例如 `feat(activity): 新增活动报名人数限制`、`fix(venue): 修复场地预约冲突`、`docs: 更新 README`。允许的 type 见 `CONTRIBUTING.md` → Commit 信息。
- 创建 Issue 或 PR 时，遵循 `CONTRIBUTING.md` → Issue 与 PR 标签规范。**类型、优先级、领域三个标签必须同时标注**。创建前先运行 `gh label list` 获取仓库当前所有可用标签，以实际输出为准。
- 创建 PR 时，`gh pr create` 必须附带 `--label` 参数一次性标注；创建 Issue 时，`gh issue create` 必须附带 `--label` 参数一次性标注，并使用 `--template` 指定正确的模板文件。
- PR 模板位于 `.github/pull_request_template.md`，创建 PR 前应读取该模板，按模板中的章节逐项填写。

## Agent 操作约束

以下约束**仅针对 Agent**（Claude Code 等），人类组员不受限制：

- Agent **禁止**在未经用户明确许可的情况下执行 `git commit` 或 `git push`，并且**绝对禁止对 dev 和 main 分支使用 `push -f`**，且**非必要情况下不准使用 `push -f`**。
- 所有修改完成后，Agent 必须先将修改内容汇报给用户，获得用户确认后才可以提交。
- 倘若 Agent 修改 CI/CD 工作流、项目结构、分支策略、开发流程后，**必须同步更新对应的文档**（`CONTRIBUTING.md`、`AGENTS.md`、`README.md`），保持代码和文档一致。
- Agent 必须遵守 API-first 模式：修改 API 时先改 `api/openapi.yaml`，不允许在 Controller 里直接硬编码新的请求/响应模型。
- Agent 禁止手改 `frontend/src/api/*` 和 `backend/Models/*`。
- **新分支命名**：分支名带上关联 Issue 编号，格式为 `类型/编号-简短描述`，例如 `feature/42-activity-checkin`、`fix/56-venue-conflict`、`docs/55-pr-title-format`。允许的类型前缀见 `CONTRIBUTING.md` → 分支要求。
- **新分支立即开 draft PR**：创建分支后，Agent 应立即用 `gh pr create --draft` 创建 draft PR。代码完成后标记 Ready for Review。
- **CodeRabbit review**：PR 标记 Ready 后，CodeRabbit 会自动 review。Agent 必须读取 review 意见：合理的修改直接采纳并 push，不合理的在 PR 评论中回复说明原因。

## 提交前

- 本次修改能对应到一个功能点、文档任务、数据库任务或基础设施任务。
- 数据库若改动，已同步脚本、迁移说明和文档。
- 新增业务逻辑有测试或手工验证说明。
- 工作已 Issue/PR/commit。
