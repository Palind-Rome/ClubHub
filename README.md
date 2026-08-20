# ClubHub 高校社团运营与协同管理平台

ClubHub 是《数据库课程设计》项目，面向高校社团日常运营场景，计划实现社团组织管理、成员招募、活动与场地、项目协作、课程资源、运营评价、公告通知和讨论区等功能。

本项目采用 C# / Visual Studio / Oracle 技术路线，目标实现为前后端分离的 ASP.NET Core B/S 系统，便于多人协作、网页演示和后续部署。

## 技术栈

- IDE：Visual Studio Community 2022 或更高版本
- 后端：C# / ASP.NET Core 10 Web API（目标框架 `net10.0`）
- 前端：Vue 3 / Vite
- 数据库：Oracle Database 18c 或更高版本
- 数据访问：Oracle Managed Data Access / ODP.NET，必要时使用 Oracle EF Core Provider
- 缓存与协调：Redis / StackExchange.Redis（按功能开关启用）
- 协作：GitHub Issues / Pull Requests / GitHub Actions

## 目录结构

```text
.
├── .github/          # Issue 模板、PR 模板、CI 和部署 workflow
├── api/              # OpenAPI 规范文件，用于生成 API 客户端代码
├── backend/          # ASP.NET Core Web API
├── backend.Tests/    # 不连接远程 Oracle 的后端单元测试与 API 边界测试
├── backend.OracleIntegrationTests/ # 仅用于隔离 Oracle Schema 的专属集成测试
├── database/         # Oracle 建表脚本、种子数据、视图、迁移说明
├── docs/             # 课程交付文档
├── frontend/         # Vue 3 / Vite 前端
├── AGENTS.md         # 给 Agent 阅读的开发约定
├── CONTRIBUTING.md   # 协作说明
└── README.md
```

## 设计文档

- [Redis 数据边界与故障降级基线](docs/architecture/redis-data-boundary.md)：规定后续
  Redis 子任务统一使用的数据分类、Key、TTL、持久化、故障降级和回滚边界。

## Redis 后端基础组件

直接运行后端时 Redis 默认关闭。使用 Docker 开发环境时，先复制 `.env.example`
为 `.env` 并设置本机专用 `REDIS_PASSWORD`，再运行
`docker compose -f docker-compose.dev.yml up`。Redis 只在容器网络内开放，宿主机
不映射 6379；业务缓存和可靠状态能力分别由 `REDIS_CACHE_ENABLED`、
`REDIS_AUTH_SESSIONS_ENABLED`、`REDIS_PERMISSION_CACHE_ENABLED`、
`REDIS_PREVIEW_SESSIONS_ENABLED`、`REDIS_RATE_LIMITING_ENABLED` 与
`REDIS_IDEMPOTENCY_ENABLED` 控制，默认全部关闭。

截至 2026-08-20，production 暂时不部署 Redis：生产 `docker-compose.yml` 不创建
Redis service，backend 显式使用 `Redis__Enabled=false`，Deploy workflow 不再要求或
传递 `REDIS_PASSWORD` / Redis feature vars，并会清理服务器 `.env` 中遗留的 Redis
配置。开发与 CI 的 Redis 支持保持不变；未来恢复 production Redis 前需先确定稳定的
镜像来源并按运维手册恢复 service、Secret 与 readiness 依赖。

统一连接、Key、序列化和 Cache Aside 实现在 `backend/Infrastructure/Redis/`。
`GET /health/live` 只检查 API 进程；`GET /health/ready` 还会检查已启用的 Redis。
启用 `REDIS_CACHE_ENABLED` 后，活动详情和场地详情使用统一缓存；活动报名人数和
当前用户报名状态仍实时查询 Oracle。Redis 超时或断连时查询自动回源 Oracle，
Oracle 写入成功后再失效对应详情缓存。
Issue #156 还提供 Redis 登录会话 allowlist、5 分钟权限快照、跨实例预览会话、
固定窗口限流和 Oracle 幂等台账。启用幂等前必须先人工执行
`database/migrations/20260726_add_idempotency_records.sql`；最后启用认证会话，
启用时已有纯签名 Token 将失效并要求重新登录。
部署、备份、恢复、升级、密码轮换和排障步骤见
[Redis 运维手册](docs/operations/redis-runbook.md)。

## 课程要求摘要

- 使用较新版本 VS.NET / Visual Studio。
- 使用 C#。
- 使用 Oracle 18c 或更高版本。
- 使用 Oracle 数据访问组件或 ORM 框架。
- 至少 12 张表，且符合第三范式。
- 至少 20 个功能点，其中至少 15 个必须有业务逻辑。
- 最终提交系统需求分析文档、数据库设计文档、系统设计与实现文档、答辩 PPT，并完成项目答辩和演示。

## 协作与环境

1. 阅读 `CONTRIBUTING.md`，确认环境、分支、提交、Issue、PR、CI/CD 和安全规范。
2. 配好 Visual Studio、.NET SDK 10.0、Oracle XE、SQL Developer。
3. 用 `database/schema.sql` 创建本地数据库结构，用 `database/verify.sql` 验证。
4. 日常开发先从 `dev` 分支开功能分支，用 Issue、PR 和 commit 留痕。

## 自动化测试

后端测试统一使用 xUnit。API 测试通过 `ClubHubWebApplicationFactory` 将正式 Oracle
`DbContext` 替换为进程内测试数据库，不读取或修改团队共享的远程 Oracle：

```powershell
dotnet test ClubHub.sln --configuration Release
```

前端使用 Vitest 和 jsdom，HTTP 请求使用 Mock，不依赖后端或数据库：

```powershell
cd frontend
pnpm install --frozen-lockfile
pnpm test
```

CI 会在相关目录发生变更时自动运行对应测试。需要验证 Oracle sequence、迁移脚本或
Oracle 特有查询时，应另行使用隔离的测试 Schema 或一次性数据库，禁止使用共享开发库。
`backend.OracleIntegrationTests` 默认跳过；只有同时提供
`CLUBHUB_ORACLE_INTEGRATION_CONNECTION` 和
`CLUBHUB_ORACLE_INTEGRATION_ISOLATED=true` 时才会执行，具体见该目录的 README。
