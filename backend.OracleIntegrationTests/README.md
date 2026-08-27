# Oracle 集成测试

本项目只验证 Oracle 专属行为，不属于使用内存数据库的 `backend.Tests`。
测试会在当前 Schema 中创建并删除名称以 `CH_TX_` 开头的临时表，因此只能连接
隔离测试 Schema 或一次性 Oracle 数据库，禁止连接共享开发、演示或生产 Schema。
答辩数据回归测试还要求该隔离 Schema 已执行 `database/schema.sql` 及
`000_sample_users.sql`、`001_sample_clubs.sql`、`005_sample_member_terms.sql`；测试会连续执行两次
`008_defense_demo.sql`，验证幂等性、标题保留和五类审计条件，并清理自己的探针记录。

默认执行 `dotnet test ClubHub.sln` 时，这些测试会显示为跳过。确认目标数据库隔离后，
设置以下环境变量再单独运行：

```powershell
$env:CLUBHUB_ORACLE_INTEGRATION_CONNECTION = "User Id=...;Password=...;Data Source=..."
$env:CLUBHUB_ORACLE_INTEGRATION_ISOLATED = "true"
dotnet test backend.OracleIntegrationTests --configuration Release
```

连接串不得提交到仓库。`CLUBHUB_ORACLE_INTEGRATION_ISOLATED=true` 是显式安全确认，
不能替代对目标 Schema 的人工核对。
