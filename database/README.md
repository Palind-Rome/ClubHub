# 数据库目录

本目录保存 ClubHub 的 Oracle 数据库脚本。

- `schema.sql`：第一次数据库设计作业形成的建表脚本。
- `verify.sql`：验证当前用户、表数量和表名。
- `smoke_test.sql`：验证应用用户可以 SELECT、INSERT 并回滚。
- `seeds/`：后续放演示数据。
- `views/`：后续放统计视图。
- `migrations/`：后续放表结构演进说明或迁移脚本。

## 本地初始化

在 SQL Developer 中打开 `schema.sql`，确认连接下拉框选择 `CLUBHUB-XEPDB1`，然后用 F5 运行脚本。

不要用管理员连接执行建表脚本；建表脚本应该运行在 `CLUBHUB` 用户下。

## 验证

在 SQL Developer 中打开并执行 `verify.sql`。

如果需要确认数据库账号具有基本读写能力，执行 `smoke_test.sql`。该脚本会插入一条临时数据并立即回滚，正常情况下不会留下测试数据。

当前已验证 `CLUBHUB` 用户下共有 22 张表。

## 修改规则

- 使用 Oracle 语法。
- 表结构变更必须同步 `schema.sql` 和数据库设计文档。
- 新增种子数据、视图、迁移脚本时放入对应子目录。
