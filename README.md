# ClubHub 高校社团运营与协同管理平台

ClubHub 是《数据库课程设计》项目，面向高校社团日常运营场景，计划实现社团组织管理、成员招募、活动与场地、项目协作、课程资源、运营评价、公告通知和讨论区等功能。

本项目采用 C# / Visual Studio / Oracle 技术路线，目标实现为前后端分离的 ASP.NET Core B/S 系统，便于多人协作、网页演示和后续部署。

## 技术栈

- IDE：Visual Studio Community 2022 或更高版本
- 后端：C# / ASP.NET Core 10 Web API（目标框架 `net10.0`）
- 前端：Vue 3 / Vite
- 数据库：Oracle Database 18c 或更高版本
- 数据访问：Oracle Managed Data Access / ODP.NET，必要时使用 Oracle EF Core Provider
- 协作：GitHub Issues / Pull Requests / GitHub Actions

## 目录结构

```text
.
├── .github/          # Issue 模板、PR 模板、CI 和部署 workflow
├── api/              # OpenAPI 规范文件，用于生成 API 客户端代码
├── backend/          # ASP.NET Core Web API
├── database/         # Oracle 建表脚本、种子数据、视图、迁移说明
├── docs/             # 课程交付文档
├── frontend/         # Vue 3 / Vite 前端
├── AGENTS.md         # 给 Agent 阅读的开发约定
├── CONTRIBUTING.md   # 协作说明
└── README.md
```

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
