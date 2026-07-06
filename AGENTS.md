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
- `docs/`：只放课程最终交付文档，不放临时环境笔记、日志或协作规范。

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

当前 CI 包含三个 Job（详情见 `CONTRIBUTING.md`）：

| Job | 说明 |
|-----|------|
| `validate` | 检查仓库必要文件和目录、数据库脚本至少 12 张表。 |
| `build-backend` | 如果存在 `.sln`，自动 restore/build。 |
| `build-frontend` | 如果存在 `frontend/package.json`，用 pnpm 安装依赖并构建（强制要求 lockfile）。 |

后续补充：

- 测试步骤（`dotnet test`、`pnpm test`），待项目建立后启用。
- Oracle 远程语法验证（`sqlplus` 连接远端 Oracle 执行 schema.sql），待远程 Oracle 实例和 GitHub Secrets 就绪后启用。

部署 workflow 目前为手动模板，等服务器和可运行应用都准备好后会启用自动触发。

## 提交前

- 本次修改能对应到一个功能点、文档任务、数据库任务或基础设施任务。
- 数据库若改动，已同步脚本、迁移说明和文档。
- 新增业务逻辑有测试或手工验证说明。
- 工作已 Issue/PR/commit。
