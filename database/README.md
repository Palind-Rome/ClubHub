# 数据库目录

本目录保存 ClubHub 的 Oracle 数据库脚本。

- `schema.sql`：第一次数据库设计作业形成的建表脚本。
- `verify.sql`：验证当前用户、表数量和表名。
- `seeds/`：后续放演示数据。
- `views/`：后续放统计视图。
- `migrations/`：后续放表结构演进说明或迁移脚本。

## 后端连接数据库

ASP.NET Core 后端通过 EF Core + Oracle 驱动连接远程 Oracle，**不需要在本地安装 Oracle 客户端**。

### 配置方法

1. 复制 `backend/appsettings.Development.example.json` 为 `backend/appsettings.Development.json`（该文件已加入 `.gitignore`，不会提交到仓库）。
2. 填入远程 Oracle 的连接信息：

```json
{
  "ConnectionStrings": {
    "Default": "User Id=CLUBHUB;Password=你的密码;Data Source=//服务器IP:1521/服务名"
  }
}
```

3. `dotnet run --project backend` 启动后端，自动连接数据库。

### 原理

- NuGet 包 `Oracle.EntityFrameworkCore`（已安装）内含 `Oracle.ManagedDataAccess.Core`，是 Oracle 官方提供的全托管驱动，直接通过网络连接远程 Oracle，不依赖本地 Oracle 客户端。
- 连接字符串中 `Data Source` 的格式为 `//主机:端口/服务名`，例如 `//10.0.0.5:1521/CLUBHUB`。

## 修改规则

- 使用 Oracle 语法。
- 表结构变更必须同步 `schema.sql` 和数据库设计文档。
- 新增种子数据、视图、迁移脚本时放入对应子目录。
