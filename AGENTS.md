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

## 完整开发流程

本节覆盖从本地编码到上线部署的**全部步骤**。所有组员和 Agent 必须遵守。

### 一、分支策略

```
main  ─── 生产分支，只接受 dev 的 PR 合并。push main 自动部署到服务器。
dev   ─── 集成分支，接受 feature/fix/docs/db 分支的 PR。
feature/xxx ─── 功能开发分支，从 dev 最新 commit 拉出。
fix/xxx     ─── 缺陷修复分支
docs/xxx    ─── 文档分支
db/xxx      ─── 数据库分支
```

规则：
- **不要直接在 main 和 dev 上提交。**
- dev 和 main 的分支保护已开启：必须通过 PR + CI 通过 + CODEOWNERS 一人 approve 才能合并。
- draft PR 不触发 CI，标记 ready 后才会跑。

### 二、本地开发流程（一个功能点的完整路径）

```
 1. git checkout dev && git pull origin dev
    git checkout -b feature/your-task
       │
 2. 立即创建 draft PR（feature/your-task → dev）：
    gh pr create --draft --base dev --title "feat(scope): 功能名称"
    → CI 不会在 draft 阶段运行，等代码写好再 mark ready
       │
 3. 本地编码（一次性完成）：
    ├── api/openapi.yaml            ← 新增/修改端点
    ├── backend/Controllers/*.cs    ← API 路由
    ├── backend/Services/*.cs       ← 业务逻辑
    └── frontend/src/...            ← Vue 页面和组件
       │
 4. 本地验证（必须通过才能 push）：
    dotnet restore && dotnet build --configuration Release
    cd frontend && pnpm install --frozen-lockfile && pnpm run build
       │
 5. git add 具体文件名（禁止 git add .）
    git commit -m "feat(scope): 中文摘要"
    git push -u origin feature/your-task
       │
 6. gen-api-code.yml 自动触发（仅当 api/openapi.yaml 有变更时）
    → CI 生成 backend/Models/* 和 frontend/src/api/*
    → CI 自动 commit 回你的 feature 分支
       │
 7. git pull 拉取 CI 生成的代码
       │
 8. 再次本地验证（确保生成的代码能编译通过）
       │
 9. 代码准备好后，将 draft PR 标记为 Ready for Review
    → CI 开始运行，CodeRabbit 自动 review
       │
10. 阅读 CodeRabbit 的 review 意见：
    ├── 合理的 → 根据意见修改代码，commit + push
    └── 不合理的 → 在 PR 评论区回复说明原因
       │
11. CI 全部通过 + CodeRabbit 无阻塞性问题 + CODEOWNERS approve → Merge
```

### 三、PR 门禁（feature → dev）

```
发起 PR 后自动触发：

┌─ ci.yml ──────────────────────────────────────────┐
│  validate        检查文件完整性 + schema ≥12 张表    │
│  build-backend   如果有 .sln 则 dotnet build        │
│  build-frontend  如果有 package.json 则 pnpm build  │
└────────────────────────────────────────────────────┘
┌─ code-check.yml ───────────────────────────────────┐
│  pre-commit 通用检查（行尾空格、YAML 语法等）        │
└────────────────────────────────────────────────────┘

两项全部通过 + CODEOWNERS 一人 approve → 可以 Merge
```

### 四、阶段性上线（dev → main）

```
dev 积累到里程碑节点（课程答辩/演示版本）：

 1. 发起 PR：dev → main
 2. 同样跑 ci.yml + code-check.yml
 3. 全部通过 + CODEOWNERS 一人 approve → Merge
 4. Merge 后 main 收到新 commit
       │
       ▼
    deploy.yml 自动触发
       │
    ├── docker build backend/Dockerfile  → ghcr.io/.../clubhub-backend:latest
    ├── docker build frontend/Dockerfile → ghcr.io/.../clubhub-frontend:latest
    ├── docker push 两个镜像
    ├── SCP docker-compose.yml → 服务器
    └── SSH 服务器执行：
          docker compose pull
          docker compose up -d
          健康检查 ✅
       │
       ▼
    🖥️ 服务器更新为最新版本，对外可访问
```

### 五、Commit 格式

```
<type>(可选 scope): <简短中文摘要>

* 做了什么：……
* 为什么：……
* 影响范围：……
* 注意事项：……
```

| type | 用途 |
|------|------|
| `feat` | 新功能 |
| `fix` | 修复 bug |
| `docs` | 仅文档 |
| `style` | 格式调整 |
| `refactor` | 重构 |
| `perf` | 性能优化 |
| `test` | 测试 |
| `build` | 构建/依赖/Docker |
| `ci` | CI/CD |
| `chore` | 维护 |
| `revert` | 回滚 |

Scope 示例：`activity`、`venue`、`club`、`auth`、`deploy`、`docker`。

### 六、代码分区（什么能改，什么不能改）

| 目录 | 权限 | 说明 |
|------|------|------|
| `api/openapi.yaml` | ✅ 能改 | API 契约源头 |
| `backend/Controllers/*` | ✅ 能改 | 手写 API 路由 |
| `backend/Services/*` | ✅ 能改 | 手写业务逻辑 |
| `frontend/src/`（除 api/） | ✅ 能改 | 手写 Vue 组件 |
| `frontend/src/api/*` | ❌ 禁止手改 | CI 自动生成 |
| `backend/Models/*` | ❌ 禁止手改 | CI 自动生成 |

> **为什么**：改 `api/openapi.yaml` → push → CI 自动生成 Models 和前端 API 代码。
> 如果你手改了这些自动生成的文件，下次 CI 运行会覆盖你的改动。

### 七、本地环境

```bash
# 方式一：直接跑（需安装 .NET SDK + Node.js）
dotnet run --project backend          # 后端 → localhost:5000
cd frontend && pnpm run dev           # 前端 → localhost:5173

# 方式二：Docker（只需 Docker，不需要装 SDK/Node）
docker compose -f docker-compose.dev.yml up   # 一键启动，源码热重载
```

### 八、常用命令速查

```powershell
# 后端
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release

# 前端
corepack enable
pnpm install --frozen-lockfile
pnpm run lint
pnpm run build

# Docker
docker compose -f docker-compose.dev.yml up    # 本地开发
docker compose build                            # 构建生产镜像
docker compose up -d                            # 生产启动
```

## Agent 操作约束

以下约束**仅针对 Agent**（Claude Code 等），人类组员不受限制：

- Agent **禁止**在未经用户明确许可的情况下执行 `git commit` 或 `git push`。
- 所有修改完成后，Agent 必须先将修改内容汇报给用户，获得用户确认后才可以提交。
- Agent 修改 CI/CD 工作流、项目结构、分支策略、开发流程后，**必须同步更新对应的文档**（`CONTRIBUTING.md`、`AGENTS.md`、`README.md`），保持代码和文档一致。
- Agent 必须遵守 API-first 模式：修改 API 时先改 `api/openapi.yaml`，不允许在 Controller 里直接硬编码新的请求/响应模型。
- Agent 禁止手改 `frontend/src/api/*` 和 `backend/Models/*`。
- **新分支立即开 draft PR**：创建 feature/fix 分支后，Agent 应立即用 `gh pr create --draft` 创建 draft PR。代码完成后标记 Ready for Review。
- **CodeRabbit review**：PR 标记 Ready 后，CodeRabbit 会自动 review。Agent 必须读取 review 意见：合理的修改直接采纳并 push，不合理的在 PR 评论中回复说明原因。

## 提交前

- 本次修改能对应到一个功能点、文档任务、数据库任务或基础设施任务。
- 数据库若改动，已同步脚本、迁移说明和文档。
- 新增业务逻辑有测试或手工验证说明。
- 工作已 Issue/PR/commit。
