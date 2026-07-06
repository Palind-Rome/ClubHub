# ClubHub 组员协作说明

本文档写给所有组员。目标是让大家知道：怎么开任务、怎么写代码、怎么提交、怎么留下贡献证据，以及哪些事情不要一开始做复杂。

## TL;DR

- 我们采用 GitHub Issues + Pull Requests + CI 的轻量协作流程。
- `main` 保存阶段性稳定版本，`dev` 用作日常集成，个人任务从 `dev` 拉功能分支。
- 功能分支可以 `fetch + rebase`。
- 密码、私钥、服务器 IP、Oracle 连接串不进仓库，只放本机环境变量或 GitHub Secrets。
- 前后端分离：后端 C# / ASP.NET Core Web API，前端 Vue 3 / Vite，数据库 Oracle。

## 仓库目录

- `.github/`：Issue 模板、PR 模板、CI 和部署 workflow。
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
  - Require approvals：至少 1 人。
  - Require status checks to pass：选择 `CI`。
  - Require conversation resolution before merging。
  - Do not allow force pushes。
  - Do not allow deletions。
- `dev`
  - Require a pull request before merging。
  - Require status checks to pass：选择 `CI`。
  - Require approvals 可以先设 1 人；
  - Do not allow force pushes。

这样做不是为了增加仪式感，而是为了防止主分支被误推坏，同时让贡献记录可追踪。

## 日常开发流程

开始任务：

```powershell
git fetch origin
git checkout dev
git pull --ff-only origin dev
git checkout -b feature/your-task
```

提交修改：

```powershell
git status
git add 具体文件名
git commit -m "[Feature] 新增活动报名人数限制"
```

不要使用 `git add .`。推送分支：

```powershell
git push -u origin feature/your-task
```

然后在 GitHub 上发起 PR，目标分支选 `dev`。

## fetch + rebase

个人功能分支推荐用 `fetch + rebase` 跟上 `dev`：

```powershell
git fetch origin
git rebase origin/dev
```

不建议 rebase 的情况：

- `main` 和 `dev` 这种公共分支。
- 多个人已经共同基于同一个远程分支提交。
- 你不确定冲突怎么处理。

原则：个人分支可以整理历史，公共分支不要改历史。

## Pull Request 规则

每个 PR 要写清楚：

- 本次做了什么。
- 对应哪个 Issue 或课程功能点。
- 涉及哪些数据库表和接口。
- 如何验证。
- 是否需要更新课程文档。
- 是否影响部署或环境变量。

合并规则：

- 功能分支默认 PR 到 `dev`。
- `dev` 到 `main` 只在阶段性节点合并。
- 至少让 1 名组员看过再合并；数据库结构和核心业务逻辑最好让 2 人看。
- CI 失败时不要合并。

## Commit 信息

提交信息可以用中文。例子：

```text
[Feature] 新增活动报名人数限制
[Fix] 修复签到时间窗判断
[DB] 补充数据库验证脚本
[Docs] 完成需求分析文档初稿
[Refactor] 拆分活动服务逻辑
[Test] 增加场地冲突检测用例
```

## 数据库规则

- 表结构基线是 `database/schema.sql`。
- 验证脚本是 `database/verify.sql`。
- 新增演示数据放 `database/seeds/`。
- 新增统计视图放 `database/views/`。
- 新增结构变更放 `database/migrations/`。
- 表结构变更必须同步数据库设计文档。
- 修改表名、字段名、主外键前先在群里说明原因。
- SQL 必须使用 Oracle 语法，不混用 MySQL / SQL Server 写法。

CI 不自动全量刷新数据库，不自动重建生产索引。索引是否新增、删除或调整，要通过迁移脚本和 PR review 决定。全量刷新只用于本地开发库或明确的测试库。

## CI/CD 策略

当前是基础 CI：

- 检查仓库必要文件。
- 检查数据库脚本至少 12 张表。
- 检查 `varchar2` 是否有长度。
- 如果后续出现 `.sln`，自动执行 `dotnet restore` 和 `dotnet build`。
- 如果后续出现 `frontend/package.json`，自动执行前端安装和构建。

部署 workflow 已预留为手动模板。意思是：现在它只会在 GitHub Actions 页面手动点击 `Run workflow` 时运行，不会因为 push 或 PR 自动部署。

## 文档规则

`docs/` 只放最终要交的课程文档：

- 系统需求分析文档
- 数据库设计文档
- 系统设计与实现文档
- 答辩 PPT

临时想法、协作规范、环境说明不要放进 `docs/`。