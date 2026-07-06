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
- `frontend/`：Vue 3 + Vite 或同等轻量前端，负责页面和交互。
- `database/`：Oracle 建表脚本、验证脚本、种子数据、视图和迁移脚本。
- `docs/`：各类文档。

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

## 开发规则

请阅读 `CONTRIBUTING.md`。

**Agent 操作约束**：Agent **禁止**在未经用户明确许可的情况下执行 `git commit` 或 `git push`。
所有修改完成后，Agent 必须先将修改内容汇报给用户，获得用户确认后才可以提交。

**文档同步规则**：对 CI/CD 工作流、项目结构、分支策略、开发流程做出修改后，
必须同步更新对应的文档（`CONTRIBUTING.md`、`AGENTS.md`、`README.md`），
保持代码和文档一致。

## 常用检查命令

后端项目出现后：

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

前端项目出现后：

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm run lint
pnpm run build
```

如果项目暂时没有对应目录或命令，不要硬造脚本。先保持 CI 能通过，再在引入项目骨架时同步补命令。

## CI/CD 规则

项目有 4 条流水线（详情见 `CONTRIBUTING.md`）：

| 工作流 | 触发条件 | 作用 |
|--------|----------|------|
| `ci.yml` | PR / push 到 `main` `dev` | 项目结构校验、后端构建、前端构建 |
| `code-check.yml` | PR 到 `main` `dev` | 代码质量门禁（通用 pre-commit 检查） |
| `gen-api-code.yml` | push 到非 `main` `dev` 分支且 `api/` 变更 | 从 OpenAPI 契约自动生成前后端代码 |
| `deploy.yml` | push `main` 自动部署 / 手动触发可选环境 | 部署到服务器 |

后续补充：

- 测试步骤（`dotnet test`、`pnpm test`），待项目建立后启用。
- Oracle 远程语法验证（`sqlplus` 连接远端 Oracle 执行 schema.sql），待远程 Oracle 实例和 GitHub Secrets 就绪后启用。

## API-first 开发（Agent 操作指引）

ClubHub 采用 API-first 模式。Agent 在开发功能时必须遵循以下流程：

### 修改 API

1. **修改 `api/openapi.yaml`**：新增端点、修改 Schema、添加字段说明。
2. **同时编写 Controller 和 Service**：Agent 在 `backend/Controllers/` 写路由，在 `backend/Services/` 写业务逻辑。Controller 可以随契约一起写好再 push，不需要等 CI。
3. **Push 后 CI 生成**：`gen-api-code.yml` 自动生成 `backend/Models/` 和 `frontend/src/api/` 并 commit 回分支。
4. **Pull 同步**：`git pull` 拉取 CI 生成的 Models 和前端 API 客户端代码。

### 代码分区（Agent 必须遵守）

| 目录 | 权限 | 说明 |
|------|------|------|
| `api/openapi.yaml` | ✅ 读写 | 修改 API 契约 |
| `frontend/src/api/*` | ❌ 只读 | 自动生成，禁止 Agent 手写 |
| `backend/Models/*` | ❌ 只读 | 自动生成，禁止 Agent 手写 |
| `backend/Controllers/*` | ✅ 读写 | Agent 手写 API 路由 |
| `backend/Services/*` | ✅ 读写 | Agent 手写业务逻辑 |
| `frontend/src/`（其他） | ✅ 读写 | Agent 写 Vue 组件 |

### 标准工作流

```
修改 api/openapi.yaml + 编写 Controllers/Services → git push →
CI 自动生成 Models 和前端 API 代码并 commit →
git pull 拉取生成代码 → 前端写 Vue 组件 →
git commit → git push → 发起 PR
```

Agent 可以一次性完成契约修改和 Controller/Service 编写，然后 push 触发 CI
生成 `backend/Models/` 和 `frontend/src/api/`。不需要分两次——契约和业务逻辑
本来就是一起设计的。

## 提交前

- 本次修改能对应到一个功能点、文档任务、数据库任务或基础设施任务。
- 数据库若改动，已同步脚本、迁移说明和文档。
- 新增业务逻辑有测试或手工验证说明。
- 工作已 Issue/PR/commit。
