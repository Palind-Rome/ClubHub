# Oracle 集成测试

本项目只验证 Oracle 专属行为，不属于使用内存数据库的 `backend.Tests`。
测试会在当前 Schema 中创建并删除名称以 `CH_TX_` 开头的临时表，因此只能连接
隔离测试 Schema 或一次性 Oracle 数据库，禁止连接共享开发或生产 Schema。
数据质量巡检测试要求该隔离 Schema 已执行 `database/schema.sql`；测试会运行
`009_data_quality_audit.sql`，确认基线库不存在占位标题、过期状态、空关联和虚假附件。

默认执行 `dotnet test ClubHub.sln` 时，这些测试会显示为跳过。确认目标数据库隔离后，
设置以下环境变量再单独运行：

```powershell
$env:CLUBHUB_ORACLE_INTEGRATION_CONNECTION = "User Id=...;Password=...;Data Source=..."
$env:CLUBHUB_ORACLE_INTEGRATION_ISOLATED = "true"
dotnet test backend.OracleIntegrationTests --configuration Release
```

连接串不得提交到仓库。`CLUBHUB_ORACLE_INTEGRATION_ISOLATED=true` 是显式安全确认，
不能替代对目标 Schema 的人工核对。
