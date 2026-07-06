# ClubHub 组员协作说明

本文档写给所有组员。目标是让大家知道：怎么开任务、怎么写代码、怎么提交、怎么留下贡献证据，以及哪些事情不要一开始做复杂。

## TL;DR

- 我们采用 GitHub Issues + Pull Requests + CI 的轻量协作流程。
- `main` 保存阶段性稳定版本，`dev` 用作日常集成，个人任务从 `dev` 拉功能分支。
- 功能分支可以 `fetch + rebase`，但最好不要。
- 密码、私钥、服务器 IP、Oracle 连接串不进仓库，只放本机环境变量或 GitHub Secrets。
- 前后端分离：后端 C# / ASP.NET Core Web API，前端 Vue 3 / Vite，数据库 Oracle。

## 仓库目录

- `.github/`：Issue 模板、PR 模板、CI 和部署 workflow。
- `api/`：OpenAPI 规范文件，用于生成 API 客户端代码（待补充）。
- `backend/`：后端 ASP.NET Core Web API。
- `frontend/`：前端 Vue 3 / Vite。
- `database/`：Oracle 建表脚本、验证脚本、种子数据、视图、迁移说明。
- `docs/`：课程最终交付文档，包括需求分析、数据库设计、系统设计与实现、答辩 PPT。
- `AGENTS.md`：给 Codex / Claude Code 等 Agent 看的工程约定。
- `CONTRIBUTING.md`：给组员看的协作约定。

`docs/` 不放临时说明、环境笔记、工作日志或协作规范。

## 本地环境要求

- Visual Studio 2022 或更高版本。
- `ASP.NET and web development` 工作负载。
- .NET SDK。
- Oracle Database 18c 或更高版本。建议 Oracle 21c XE。
- SQL Developer 或其他 Oracle 客户端。
- 如果开发前端，再安装 Node.js LTS，并启用 Corepack / pnpm。

## 分支要求

- `main`：阶段性稳定版本。只有课程节点、演示版本、答辩版本合入。
- `dev`：日常集成分支。功能完成后先合入这里。
- `feature/xxx`：功能分支，例如 `feature/activity-checkin`。
- `fix/xxx`：缺陷修复分支，例如 `fix/venue-conflict`。
- `docs/xxx`：文档分支，例如 `docs/requirements-analysis`。
- `db/xxx`：数据库变更分支，例如 `db/add-seed-data`。

不要直接在 `main` 上提交。`dev` 通过 PR 合入。

如果你使用 Codex、Claude Code 等 Agent 创建分支，先确认它不会强制套用不合适的分支名前缀。如果前缀冲突，先提醒使用者处理。

## GitHub 规则

已设定规则

`Settings` -> `Branches` 或 `Rules` -> 新建 branch protection rule / ruleset。

- `main`
  - Require a pull request before merging。
  - Require approvals：CODEOWNERS 中列出的任意一人 approve 即可。
  - Require status checks to pass：选择 `validate`、`build-backend`、`build-frontend`、`code-check`。
  - Require conversation resolution before merging。
  - Do not allow force pushes。
  - Do not allow deletions。
- `dev`
  - Require a pull request before merging。
  - Require status checks to pass：选择 `validate`、`build-backend`、`build-frontend`、`code-check`。
  - Require approvals：CODEOWNERS 中列出的任意一人 approve 即可。
  - Do not allow force pushes。


## 日常开发流程

开始任务：

拉取最新的 `dev` 分支，在 `dev` 分支上的最新的 commit 上开新分支进行工作：

```bash
git fetch origin
git checkout dev
git pull origin dev
git checkout -b feature/your-task
```

提交修改：

```bash
git status
git add 具体文件名
git commit -m "feat(activity): 新增活动报名人数限制"
```

**不要使用 `git add .`。**

推送分支：

```bash
git push -u origin feature/your-task
```

然后发起 PR，目标分支选 `dev`。发起 PR 推荐 AGENT 使用 `gh` CLI 进行全自动工作流，也可以手动在 GitHub 网页端发起 PR。


## Pull Request 规则

每个 PR 写清楚的内容：参考 `.github/pull_request_template.md`。

合并规则：

- 功能分支默认 PR 到 `dev`。
- `dev` 到 `main` 只在阶段性节点合并。
- PR Review Rule 参考前文所述的 `.github/CODEOWNERS` 中的规则。
- CI 失败时禁止合并。

## Commit 信息

使用两段式 commit message。

第一行必须使用 Conventional Commits 格式：

```text
<type>(可选 scope): <简短中文摘要>
```

然后空一行。

空行之后，写一段详细的中文 commit message。

允许使用的 type：

* feat：新功能
* fix：修复 bug
* docs：仅文档变更
* style：不影响代码行为的格式调整
* refactor：既不修复 bug，也不新增功能的代码重构
* perf：性能优化
* test：新增或更新测试
* build：构建系统、依赖、包管理或 Docker 相关变更
* ci：CI/CD 相关变更，包括 GitHub Actions
* chore：不属于以上类型的维护性任务
* revert：回滚之前的提交

第一行规则：

* `type` 必须使用上面列出的英文小写类型。
* `scope` 可选，用于说明影响范围，例如 `activity`、`venue`、`club`、`auth`。
* 冒号 `:` 前面的部分保持英文，例如 `feat(activity)`、`fix(venue)`、`ci(deploy)`。
* 冒号 `:` 后面的摘要使用中文。
* 摘要应简洁，建议整行不超过 72 个字符。
* 中文摘要建议使用动宾结构，例如“新增……”“修复……”“更新……”“移除……”。

示例：

* `feat(activity): 新增活动报名人数限制`
* `fix(venue): 修复场地预约时间冲突判断`
* `ci(deploy): 新增 GitHub Actions SSH 部署工作流`
* `build(deps): 升级 Oracle EF Core 到 9.x`

中文详情部分规则：

* 使用中文。
* 说明具体做了什么。
* 在有必要时说明为什么做这个改动。
* 提到重要的影响模块、文件或行为。
* 如果有破坏性变更、迁移步骤或部署注意事项，需要明确说明。
* 不要写“修改了一些代码”“优化项目”这类模糊描述。
* 详情部分保持简洁，通常使用 2–5 条 bullet points。

推荐格式：

```text
<type>(可选 scope): <简短中文摘要>

* 做了什么：……
* 为什么：……
* 影响范围：……
* 注意事项：……
```

示例：

```text
feat(activity): 新增活动签到功能

* 做了什么：新增活动签到 API 和签到记录表，支持活动现场扫码或手动签到。
* 为什么：社团需要在活动现场确认成员实际出勤情况。
* 影响范围：ACTIVITIES、ACTIVITY_PARTICIPATIONS、新增 ATTENDANCE_RECORDS 表。
* 注意事项：签到接口需要验证用户已报名该活动，重复签到返回已有记录。
```

```text
ci(deploy): 新增 GitHub Actions SSH 部署流程

* 做了什么：新增 GitHub Actions 工作流，通过 SSH 登录服务器并自动拉取、构建、重启服务。
* 为什么：减少手动部署步骤，保证每次推送后都能以一致流程发布。
* 影响范围：影响部署流程、服务器目录结构和环境变量配置。
* 注意事项：需要在 GitHub Secrets 中配置服务器地址、用户名、SSH 私钥和部署路径。
```

```text
feat(club): 新增社团成员角色管理

* 做了什么：新增社团内角色分配和权限检查逻辑，支持社长、副社长、普通成员三种角色。
* 为什么：社长需要给不同成员分配管理权限，区分操作范围。
* 影响范围：CLUBS、CLUB_MEMBERS、ROLES 表以及权限中间件。
* 注意事项：角色变更后需要刷新用户的权限缓存。
```

## 数据库规则

- 表结构基线是 `database/schema.sql`。
- 验证脚本是 `database/verify.sql`。
- 新增演示数据放 `database/seeds/`。
- 新增统计视图放 `database/views/`。
- 新增结构变更放 `database/migrations/`。
- 表结构变更必须同步数据库设计文档。
- 修改表名、字段名、主外键前先在群里说明原因。
- SQL **必须使用 Oracle 语法**，不混用 MySQL / SQL Server 写法。

CI 不自动全量刷新数据库，不自动重建生产索引。索引是否新增、删除或调整，要通过迁移脚本和 PR review 决定。全量刷新只用于本地开发库或明确的测试库。远程 Oracle 就绪后，CI 会对 `schema.sql` 做只读语法验证，不会修改数据或结构。

## CI/CD 策略

项目有 4 条 CI/CD 流水线：

| 工作流 | 触发条件 | 作用 |
|--------|----------|------|
| `ci.yml` | PR / push 到 `main` `dev` | 项目结构校验、后端构建、前端构建 |
| `code-check.yml` | PR 到 `main` `dev` | 代码质量门禁（pre-commit 检查，禁止行尾空格、YAML 语法错误、大文件等） |
| `gen-api-code.yml` | push 到非 `main` `dev` 分支且 `api/` 有变更 | 从 OpenAPI 契约自动生成前后端代码并提交回分支 |
| `deploy.yml` | 手动触发 | 部署到服务器（版本化目录 + 符号链接切换，失败自动回滚） |

CI 内部三个 Job：

| Job 名称 | 说明 |
|----------|------|
| `validate` | 检查仓库必要文件和目录、数据库脚本至少 12 张表。 |
| `build-backend` | 如果存在 `.sln`，自动 `dotnet restore` + `dotnet build`。 |
| `build-frontend` | 如果存在 `frontend/package.json`，用 `pnpm install --frozen-lockfile` + `pnpm build` 构建（强制要求 lockfile）。 |

后续补充：

- **测试步骤**：`dotnet test`、`pnpm test`，待后端/前端项目建立后启用。
- **Oracle 远程语法验证**：通过 `sqlplus` 连接远端 Oracle 实例，对 `schema.sql` 做 Oracle 语法校验（不是全量刷新），待远程 Oracle 实例和 GitHub Secrets 就绪后启用。

## API-first 开发流程

ClubHub 采用 API-first 开发模式：**先定义 API 契约，再自动生成代码，最后手写业务逻辑。**

### 核心概念

`api/openapi.yaml` 是前后端的**唯一真相来源（Single Source of Truth）**。它定义了：
- 有哪些 API 端点（路径、方法、参数）
- 请求和响应的数据结构（Schema）
- 示例数据

### 代码分区：什么能改，什么不能改

| 目录 / 文件 | 能否手改 | 说明 |
|-------------|----------|------|
| `api/openapi.yaml` | ✅ **能改** | API 契约源头，所有变更从这里开始 |
| `frontend/src/api/*` | ❌ 禁止 | `gen-api-code.yml` 自动生成，手改会被覆盖 |
| `frontend/src/`（其他） | ✅ 能改 | Vue 组件、路由、状态管理 |
| `backend/Models/*` | ❌ 禁止 | `gen-api-code.yml` 自动生成，手改会被覆盖 |
| `backend/Controllers/*` | ✅ 能改 | 手写 API 路由 + 业务逻辑 |
| `backend/Services/*` | ✅ 能改 | 手写业务服务实现 |

### 开发流程

```
 1. 在 feature 分支修改 api/openapi.yaml（新增端点、修改 Schema）
          │
          ▼
 2. git push → gen-api-code.yml 自动触发
          │
          ▼
 3. OpenAPI Generator 自动生成前后端代码并 commit 回分支
          │
          ▼
 4. git pull 拉取生成的代码
          │
          ▼
 5. 手写业务逻辑：
    ├── 后端：在 Controllers/ 中实现路由调用，在 Services/ 中实现业务逻辑
    └── 前端：在 src/ 中写 Vue 组件，调用生成的 API 函数
          │
          ▼
 6. 本地验证：dotnet build / pnpm lint && pnpm build
          │
          ▼
 7. 发起 PR → CI + code-check 门禁通过 → 合并
```

> **注意**：生成的文件（`frontend/src/api/*`、`backend/Models/*`）禁止手动修改。
> 如果改了它们，下次 `gen-api-code.yml` 运行会覆盖你的改动。
> 需要修改 API 行为时，请改 `api/openapi.yaml`，然后让流水线重新生成。

## 文档规则

`docs/` 只放最终要交的课程文档：

- 系统需求分析文档
- 数据库设计文档
- 系统设计与实现文档
- 答辩 PPT

临时想法、协作规范、环境说明不要放进 `docs/`。