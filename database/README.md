# 数据库目录

本目录保存 ClubHub 的 Oracle 数据库脚本。

- `schema.sql`：第一次数据库设计作业形成的建表脚本。
- `verify.sql`：验证当前用户、表数量和表名。
- `seeds/`：后续放演示数据。
- `views/`：后续放统计视图。
- `migrations/`：后续放表结构演进说明或迁移脚本。

### 结构迁移

生产或演示库禁止用 `schema.sql` 全量重建。已有数据库需要按时间顺序人工执行
`migrations/` 中的脚本，并在执行前备份：

1. `20260710_add_core_id_sequences.sql`：为 `USERS`、`USER_ROLES`、`CLUBS`、
   `CLUB_MEMBERS` 增加数据库生成主键和身份唯一索引。脚本会先检查重复数据，
   再把 sequence 推进到现有最大主键之后；发现重复时会停止且不会自动删改数据。

迁移完成后执行 `verify.sql`，确认核心 sequence、唯一索引及列默认值均已生效。

### 演示数据脚本

`seeds/` 下的脚本只用于本地开发库或明确的测试库，不会由 CI 自动执行。当前建议顺序：

1. `000_sample_users.sql`：补充社团申请流程需要的申请人和审核人样例。
2. `001_sample_clubs.sql`：基础社团样例。
3. `002_sample_activities.sql`：活动样例。
4. `003_sample_club_applications.sql`：社团注册申请样例，依赖 `000_sample_users.sql`。
5. `004_sample_recruitments.sql`：成员招募与报名筛选样例，依赖 `000_sample_users.sql` 和 `001_sample_clubs.sql`。

样例账号统一密码为 `123456`：

| 用户名           | 学工号    | 主要视角                                         |
| ---------------- | --------- | ------------------------------------------------ |
| `student_chen`   | `2350001` | 普通学生，提交并查看本人社团注册申请             |
| `admin_li`       | `06002`   | 社团管理员，审核注册申请并管理社团状态           |
| `president_wang` | `2250003` | 计算机协会负责人，维护社团档案和成员任期         |
| `officer_sun`    | `2350006` | 计算机协会干部，查看本社团成员任期               |
| `member_liu`     | `2450004` | 计算机协会成员，查看本人社团身份                 |
| `zhang_guoxiong` | `2350007` | 多社团学生，在不同社团分别担任成员、干部、负责人 |
| `advisor_zhang`  | `06005`   | 计算机协会指导老师，查看社团成员任期             |

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
- `schema.sql` 中核心 sequence 从 `1000000` 起步，用于避开 seeds 保留的显式演示 ID；
  已有数据库以迁移脚本根据实时最大主键计算起点为准。
