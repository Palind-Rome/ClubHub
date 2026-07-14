# 数据库目录

本目录保存 ClubHub 的 Oracle 数据库脚本。

- `schema.sql`：当前权威全量建表脚本，用于全新的本地开发库或明确测试库。
- `verify.sql`：验证当前用户、27 张核心表、项目成员、任务执行人、进度记录、社团部门和小组约束。
- `seeds/`：后续放演示数据。
- `views/`：后续放统计视图。
- `migrations/`：已有数据库的增量迁移脚本；项目成员关系依次包含 `001_add_project_members.sql` 与 `002_harden_project_members_constraints.sql`。

### 结构迁移

生产或演示库禁止用 `schema.sql` 全量重建。已有数据库需要按时间顺序人工执行
`migrations/` 中的脚本，并在执行前备份：

1. `20260710_add_core_id_sequences.sql`：为 `USERS`、`USER_ROLES`、`CLUBS`、
   `CLUB_MEMBERS` 增加数据库生成主键和身份唯一索引。脚本会先检查重复数据，
   再把 sequence 推进到现有最大主键之后，并保持至少从 `1000000` 起步；发现重复时
   会停止且不会自动删改数据。Oracle DDL 会隐式提交；若脚本因其他错误中断，修复
   原因后可安全地重新执行脚本完成剩余步骤。
2. `20260711_add_evaluation_id_sequence.sql`：为 `EVALUATIONS.evaluation_id`
   增加数据库生成主键。脚本会把 sequence 推进到现有最大主键之后，并保持至少从
   `1000000` 起步；脚本可重复执行，中断后修复原因即可安全重跑。
3. `20260712_add_activity_id_sequences.sql`：为 `ACTIVITIES.activity_id`、
   `ACTIVITY_PARTICIPATIONS.participation_id` 增加数据库生成主键。脚本会把
   sequence 推进到现有最大主键之后，并保持至少从 `1000000` 起步；脚本可重复执行，
   中断后修复原因即可重跑。执行期间必须停止仍按 `MAX(id)+1` 写入的旧后端，
   并在 `verify.sql` 验证默认值后再恢复写入。
4. `20260714_add_club_departments_groups.sql`：为 `CLUB_DEPARTMENTS`、
   `CLUB_GROUPS` 增加数据库生成主键和组织架构实体，并为 `CLUB_MEMBERS`
   增加可空 `department_id`、`group_id`。脚本会从现有 `department_name`、
   `group_name` 去重生成部门和小组并回填成员任期；若存在“有小组但没有部门”
   的历史记录，脚本会停止，需要先清理数据再迁移。迁移完成后会添加组合外键，
   保证成员不能跨社团引用部门或小组，小组也不能跨社团或跨部门引用。

迁移完成后执行 `verify.sql`，确认 sequence、唯一索引、列默认值、部门/小组外码和回填结果均已生效。

### 演示数据脚本

`seeds/` 下的脚本只用于本地开发库或明确的测试库，不会由 CI 自动执行。当前建议顺序：

1. `000_sample_users.sql`：补充社团申请流程需要的申请人和审核人样例。
2. `001_sample_clubs.sql`：基础社团样例。
3. `002_sample_activities.sql`：活动样例。
4. `003_sample_club_applications.sql`：社团注册申请样例，依赖 `000_sample_users.sql`。
5. `004_sample_recruitments.sql`：成员招募与报名筛选样例，依赖 `000_sample_users.sql` 和 `001_sample_clubs.sql`。
6. `005_sample_member_terms.sql`：计算机协会、摄影社、羽毛球协会的真实感成员与历史任期样例，依赖 `000_sample_users.sql` 和 `001_sample_clubs.sql`。
7. `006_sample_club_organizations.sql`：将上述成员任期中出现的部门和小组写入 `CLUB_DEPARTMENTS`、`CLUB_GROUPS`，并回填成员任期的 `department_id`、`group_id`。

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
| `zhao_rui`       | `2450020` | 计算机协会技术部部长，有上一学年干事任期         |
| `he_yuqing`      | `2350021` | 计算机协会宣传部部长，有上一学年社员任期         |
| `lin_kexin`      | `2250023` | 摄影社现任社长，有上一学年副社长任期             |
| `chen_moyang`    | `2350024` | 摄影社外拍部部长，有上一学年干事任期             |
| `shen_yiming`    | `2350028` | 羽毛球协会竞训部部长，有上一学年干事任期         |
| `ye_qingyang`    | `2450029` | 羽毛球协会赛事部裁判组组长，有上一学年社员任期   |

## 已有开发库迁移

已有数据库禁止重新执行全量 `schema.sql`。新增项目成员关系时按以下顺序操作：

1. 确认当前连接用户和目标 schema 是共享开发库或明确测试库，不是生产/演示库。
2. 确认 `PROJECTS`、`USERS` 已存在且 `PROJECT_MEMBERS` 尚不存在；若目标表已经存在，立即停止并检查当前结构。
3. 使用 SQL*Plus、SQLcl 或 SQL Developer 执行 `migrations/001_add_project_members.sql`。脚本会创建关系表，并将现有项目负责人回填为 active leader。
4. 再执行 `migrations/002_harden_project_members_constraints.sql`。脚本将备注列改为 255 个字符语义，并在确认无重复有效负责人后创建唯一函数索引。
5. 执行 `migrations/003_add_project_task_assignees.sql`。脚本创建多人任务执行人关系，并从既有单人任务回填数据。
6. 执行 `migrations/004_add_project_task_progress_reports.sql`。脚本创建任务进度提交记录表；既有任务不会伪造历史记录。
7. 执行 `migrations/20260714_add_club_departments_groups.sql`。若脚本提示存在没有部门的小组历史数据，先补齐或清理 `CLUB_MEMBERS.department_name` 后再重跑。
8. 执行 `verify.sql`；27 张核心表计数应为 27，重复关系、非法角色/状态、缺失负责人关系、多有效负责人、部门/小组未回填和非法组织架构引用查询均应返回 0 行。

Oracle DDL 会自动提交，迁移脚本不能被视为可事务回滚。执行前应确认连接信息并保留数据库备份；CI 不会自动执行此迁移。

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
  已有数据库的迁移脚本使用 `GREATEST(实时最大主键 + 1, 1000000)` 作为安全下限。
