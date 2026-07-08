# ClubHub 组员协作说明

本文档写给所有组员（同时也给 Agent 阅读）。目标是让大家知道：怎么开任务、怎么写代码、怎么提交、怎么留下贡献证据，以及哪些事情不要一开始做复杂。

## TL;DR

- 我们采用 GitHub Issues + Pull Requests + CI 的轻量协作流程。
- `main` 保存阶段性稳定版本，`dev` 用作日常集成，个人任务从 `dev` 拉功能分支。
- 功能分支可以 `fetch + rebase`，但最好不要。
- 密码、私钥、服务器 IP、Oracle 连接串不进仓库，只放本机环境变量或 GitHub Secrets。
- 前后端分离：后端 C# / ASP.NET Core 10 Web API，前端 Vue 3 / Vite + Element Plus，数据库 Oracle。

## 仓库目录

- `.github/`：Issue 模板、PR 模板、CI 和部署 workflow。
- `api/`：OpenAPI 规范文件，用于生成 API 客户端代码（待补充）。
- `backend/`：后端 ASP.NET Core Web API。
- `frontend/`：前端 Vue 3 / Vite。
- `database/`：Oracle 建表脚本、验证脚本、种子数据、视图、迁移说明。
- `docs/`：课程最终交付文档，包括需求分析、数据库设计、系统设计与实现、答辩 PPT。
- `docker-compose.yml`：生产环境编排，镜像来自 ghcr.io。
- `docker-compose.dev.yml`：本地开发环境，源码挂载 + 热重载。
- `AGENTS.md`：给 Codex / Claude Code 等 Agent 看的工程约定。
- `CONTRIBUTING.md`：给组员看的协作约定。


## 本地环境要求

- Visual Studio 2022 或更高版本。
- `ASP.NET and web development` 工作负载。
- .NET SDK 10.0（后端项目目标框架为 `net10.0`）。
- 远程 Oracle 数据库（团队共用，不需本地安装）。后端通过 EF Core 托管驱动直连。
  - 首次配置：复制 `backend/appsettings.Development.example.json` 为 `appsettings.Development.json`（已 gitignore），填入连接信息。详见 `database/README.md`。
- SQL Developer（可选，方便手动浏览数据库）。
- 如果开发前端，再安装 Node.js LTS，并启用 Corepack / pnpm。
- `gh` CLI（GitHub 官方命令行工具）：用于创建 PR、查看 CI 状态。未安装时请提醒用户根据用户的系统 / 环境进行安装。

## 分支要求

- `main`：阶段性稳定版本。只有课程节点、演示版本、答辩版本合入。
- `dev`：日常集成分支。功能完成后先合入这里。
- `feature/xxx`：功能分支，带关联 Issue 编号，例如 `feature/42-activity-checkin`。
- `fix/xxx`：缺陷修复分支，带关联 Issue 编号，例如 `fix/56-venue-conflict`。
- `docs/xxx`：文档分支，带关联 Issue 编号，例如 `docs/55-pr-title-format`。
- `db/xxx`：数据库变更分支，带关联 Issue 编号，例如 `db/38-add-seed-data`。

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

实行 Issue 驱动开发：每个 PR **必须**关联一个 Issue，说明"为什么做"和"做什么"。在开始任何实质性改动之前，先确认或创建对应的 Issue。

具体的判定标准（极少数的例外场景）见 `AGENTS.md` → Agent 操作约束。

一个功能点的完整路径（11 步）：

```text
 0. 确认或创建关联 Issue：
    → 搜索是否已有相关 Issue，有则记录编号
    → 没有则创建：gh issue create --template 对应模板 --label "..."
    → 如果不属于必须关联的场景，跳过此步
       │
 1. git checkout dev && git pull origin dev
    git checkout -b 类型/编号-简短描述    # 例如 feature/42-activity-checkin
       │
 2. 立即创建 draft PR（feature/your-task → dev），按标签规范带齐标签：
    gh pr create --draft --base dev \
      --title "feat(scope): 功能名称" \
      --label "课程功能点,优先级:P1,area:activity"
    （未安装 gh CLI 时：winget install GitHub.cli）
    → draft 阶段也会运行 CI 和 code-check，尽早暴露格式与编译问题
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


    → 推送后，PR 描述可能与实际提交内容不同步。
    → 此时应执行 gh pr edit 更新 PR 描述，确保"改动内容"和"关联 Issue"反映最新状态
       │
 6. gen-api-code.yml 自动触发（仅当 api/openapi.yaml 有变更时）
    → CI 生成 backend/Models/* 和 frontend/src/api/*
    → CI 自动 commit 回你的 feature 分支
       │
 7. git pull 拉取 CI 生成的代码
       │
 8. 再次本地验证（确保生成的代码能编译通过）
       │
 9. 代码准备好后，将 draft PR 标记为 Ready for Review：
    gh pr ready
    → CI / code-check 会继续随提交运行，CodeRabbit 自动 review
       │
10. 阅读 CodeRabbit 的 review 意见：
    ├── 合理的 → 根据意见修改代码，commit + push
    └── 不合理的 → 在 PR 评论区回复说明原因
       │
11. CI 全部通过 + CodeRabbit 无阻塞性问题 + CODEOWNERS approve → Merge
```


## Pull Request 规则

每个 PR 写清楚的内容：参考 `.github/pull_request_template.md`。
    
**关联 Issue**：PR 必须关联一个 Issue，在 PR 描述中写明 `Closes #123` 或 `Part of #456`（这里的数字仅做示例用）。**`Closes` 必须放在行首，不能缩进或用列表符号**，否则 GitHub 不会自动关闭 Issue。不需要提 issue 的例外场景见 `AGENTS.md` → Agent 操作约束。

**PR 标题**必须使用 Conventional Commits 格式（与 Commit 信息规范保持一致），例如：

- `feat(activity): 新增活动报名人数限制`
- `fix(venue): 修复场地预约时间冲突判断`
- `docs: 更新 README 部署说明`
- `ci(deploy): 新增 GitHub Actions SSH 部署工作流`

合并规则：

- 功能分支默认 PR 到 `dev`。
- `dev` 到 `main` 只在阶段性节点合并。
- PR Review Rule 参考前文所述的 `.github/CODEOWNERS` 中的规则。
- CI 失败时禁止合并。

## Issue 与 PR 标签规范

标签是 Issue 和 PR 分类管理的核心工具。仓库预设了以下几类标签，创建者必须按规则标注。

### 标签分类

| 分类 | 必选 | 作用 |
|------|------|------|
| **类型** | 必选其一 | 标识 Issue/PR 的性质（缺陷、功能点、文档、改进等） |
| **优先级** | 必选其一 | 标识紧急程度，用于排期；由维护者与创建者协商确定 |
| **领域** | 必选其一 | 标识涉及的业务模块，方便按模块筛选和分配任务 |
| **全栈任务** | 涉及前后端联动时必选 | 标识需要前端 + 后端 + 数据库联动的任务 |
| **状态** | 维护者管理 | 标识认领、重复、无效等处理状态 |

### 查看可用标签

具体标签值可能随项目阶段动态调整，以仓库实际配置为准。创建 Issue 或 PR 前，先用以下命令查看当前所有标签：

```bash
gh label list
```

### 创建命令

```bash
# PR（创建时直接带上标签，避免遗漏）
gh pr create --draft --base dev \
  --title "feat(scope): 标题" \
  --label "课程功能点,优先级:P1,area:activity"

# Issue（使用对应模板，按规则带齐标签）
gh issue create --template bug_report.md \
  --label "bug,优先级:P1,area:activity"
gh issue create --template feature_request.md \
  --label "课程功能点,优先级:P2,area:club"
```

### 核心规则

- 类型、优先级、领域三个标签**必须**同时标注，不可缺省；全栈任务在涉及前后端联动时也必须标注。
- 一个 Issue / PR 可以有多个标签，但类型标签只能选一个。
- **禁止创建仓库中不存在的标签**，以 `gh label list` 输出的标签为准，避免标签膨胀。
- 标签的具体使用场景和模板中的填写指引，见：

  | 文件 | 用途 |
  |------|------|
  | `.github/pull_request_template.md` | PR 模板，顶部注释列出标签指引 |
  | `.github/ISSUE_TEMPLATE/bug_report.md` | 缺陷报告 |
  | `.github/ISSUE_TEMPLATE/feature_request.md` | 课程功能点 |
  | `.github/ISSUE_TEMPLATE/doc_task.md` | 文档任务 |
  | `.github/ISSUE_TEMPLATE/enhancement.md` | 功能改进 |

## Commit 信息

使用两段式 commit message。

第一行必须使用 Conventional Commits 格式：

```text
<type>(可选 scope): <简短中文摘要>
```

然后空一行。

空行之后，写一段详细的中文 commit message。

> **PR 标题也使用同一格式**（不加空行和详情），参见 Pull Request 规则章节。

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
| `ci.yml` | PR / push 到 `main` `dev` | 项目结构校验、后端构建、前端构建，不负责格式检查 |
| `code-check.yml` | PR 到 `main` `dev` / 手动触发 | 代码质量门禁（pre-commit、前端 Prettier、后端 dotnet format） |
| `gen-api-code.yml` | push 到非 `main` `dev` 分支且 `api/` 有变更 | 从 OpenAPI 契约自动生成前后端代码并提交回分支 |
| `deploy.yml` | push `main` 自动部署 / 手动触发可选环境 | 构建 Docker 镜像 → 推送 ghcr.io → 服务器 docker compose up |

`ci.yml` 内部三个主要 Job：

| Job 名称 | 说明 |
|----------|------|
| `validate` | 检查仓库必要文件和目录、数据库脚本至少 12 张表。 |
| `build-backend` | 如果存在 `.sln`，自动 `dotnet restore` + `dotnet build`。 |
| `build-frontend` | 如果存在 `frontend/package.json`，用 `pnpm install --frozen-lockfile` + `pnpm build` 构建（强制要求 lockfile，不运行 lint）。 |

`code-check.yml` 内部主要 Job：

| Job 名称 | 说明 |
|----------|------|
| `pre-commit-check` | 始终运行通用 pre-commit 检查，如行尾空格、YAML 语法、大文件、冲突标记等。 |
| `frontend-check` | 当前端相关文件或 `code-check.yml` 变更时，安装前端依赖并运行 `pnpm run format:ci`。 |
| `backend-check` | 当后端相关文件或 `code-check.yml` 变更时，运行 `dotnet format --verify-no-changes`，并排除自动生成的 `backend/Models/**`。 |
| `code-check` | 汇总上述质量检查结果，作为 branch rule 中稳定的代码质量门禁。 |

draft PR 策略：

- `ci.yml`：draft 阶段不跳过，运行结构校验和受影响目录的构建。
- `code-check.yml`：draft 阶段不跳过，运行通用质量检查和受影响目录的语言格式检查。
- `gen-api-code.yml`：与 draft 状态无关，只按非 `main`/`dev` 分支上的 `api/**` push 或手动触发运行。
- `deploy.yml`：只在 `main` push 或手动触发时运行，与 draft PR 无关。

后续补充：

- **测试步骤**：`dotnet test`、`pnpm test`，待后端/前端项目建立后启用。
- **Oracle 远程语法验证**：通过 `sqlplus` 连接远端 Oracle 实例，对 `schema.sql` 做 Oracle 语法校验（不是全量刷新），待远程 Oracle 实例和 GitHub Secrets 就绪后启用。

### 部署 Secrets

部署 Secrets 已完成。这些 Secret 名称和服务器前置条件信息如下：

部署工作流通过 GitHub Actions 连接应用服务器。仓库配置了以下 Repository Secrets：

| Secret 名称 | 含义 |
|-------------|------|
| `SERVER_HOST` | 应用服务器公网 IP 或域名。 |
| `SERVER_PORT` | SSH 端口，默认 `22`。 |
| `SERVER_USER` | 部署用户 `deploy`，不用 root。 |
| `SERVER_SSH_KEY` | GitHub Actions 专用 SSH 私钥。 |
| `DEPLOY_PATH` | 服务器部署目录，例如 `/opt/clubhub`。 |

服务器已创建 `deploy` 用户，将其加入 `docker` 组，并保证该用户可以写入 `DEPLOY_PATH`。生产 `docker-compose.yml` 使用 `clubhub-net` 外部网络；Oracle 容器和应用容器应连接到同一个网络。不把 Oracle 1521 端口直接暴露到公网。

### PR 门禁（feature → dev）

```
发起 PR 后自动触发：

┌─ ci.yml ──────────────────────────────────────────┐
│  validate        检查文件完整性 + schema ≥12 张表    │
│  build-backend   如果有 .sln 则 dotnet build        │
│  build-frontend  如果有 package.json 则 pnpm build  │
└────────────────────────────────────────────────────┘
┌─ code-check.yml ───────────────────────────────────┐
│  pre-commit-check 通用 pre-commit 检查              │
│  frontend-check  pnpm run format:ci                │
│  backend-check   dotnet format --verify-no-changes  │
│  code-check      汇总代码质量门禁                   │
└────────────────────────────────────────────────────┘

draft PR 阶段也会运行上述门禁。全部通过 + CODEOWNERS 一人 approve → 可以 Merge
```

### 阶段性上线（dev → main）

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
 1. 本地同时编写：
    ├── api/openapi.yaml（新增端点、修改 Schema）
    ├── backend/Controllers/*（API 路由，调用 Service）
    └── backend/Services/*（业务逻辑实现）
          │
          ▼
 2. git push → gen-api-code.yml 自动触发
          │
          ▼
 3. CI 自动生成以下文件并 commit 回分支：
    ├── backend/Models/*（数据模型）
    └── frontend/src/api/*（TypeScript API 客户端）
          │
          ▼
 4. git pull 拉取生成的 Models 和前端 API 代码
          │
          ▼
 5. 前端开发：在 src/ 中写 Vue 组件，调用生成的前端 API 函数
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
> 如果只是同步了生成 workflow 的修复、但 `api/openapi.yaml` 没有新变化，可以在 GitHub Actions 中手动运行 `生成 API 代码`，选择自己的 feature 分支重新生成。
> 维护生成 workflow 时，后端 `aspnetcore` generator 使用 `aspnetCoreVersion=8.0,pocoModels=true,useNewtonsoft=false,nullableReferenceTypes=true`。不要加入 `classModifier=public`；当前生成器会直接报错。生成后会删除无用的 `Org.OpenAPITools.Converters` 引用并运行格式化，避免生成代码破坏 CI。生成模型依赖 `Newtonsoft.Json`，如果项目文件缺少该包引用，`gen-api-code.yml` 会失败并要求人工用独立提交补齐，不会自动修改 `backend/ClubHub.Api.csproj`。
> `code-check.yml` 中排除自动生成后端模型时必须写仓库相对路径 `backend/Models/**`，不能写 `Models/**`；后者不会命中生成目录。

## 本地运行

```bash
# 方式一：直接跑（需安装 .NET SDK + Node.js）
dotnet run --project backend          # 后端 → localhost:5000
cd frontend && pnpm run dev           # 前端 → localhost:5173

# 方式二：Docker（只需 Docker，不需要装 SDK/Node）
docker compose -f docker-compose.dev.yml up   # 一键启动，源码热重载
```

## 常用命令速查

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

# GitHub CLI（未安装时 winget install GitHub.cli）
gh pr create --draft --base dev --title "feat(scope): 摘要" --label "课程功能点,优先级:P1,area:activity"   # 创建 draft PR（按标签规范带齐标签）
gh pr ready                                                     # 标记为 Ready for Review
gh pr checks                                                    # 查看 CI 状态
gh pr view --comments                                           # 查看 review 意见
gh run list -b main                                             # 查看 main 的 CI 运行记录
gh run view <id> --log-failed                                   # 查看失败日志
```

## 文档规则

`docs/` 只放最终要交的课程文档：

- 系统需求分析文档
- 数据库设计文档
- 系统设计与实现文档
- 答辩 PPT

临时想法、协作规范、环境说明不要放进 `docs/`。
