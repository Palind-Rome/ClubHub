# Redis 运维手册

本文适用于 ClubHub 开发、演示以及未来重新启用 Redis 后的生产环境。Oracle 仍是正式业务
数据的唯一事实来源；Redis 中的普通缓存可以自然重建，未来的会话、幂等状态和
Stream 则依赖 AOF、备份和业务对账。

## 当前生产状态（2026-08-20）

production Redis **暂时停用**。当前 `docker-compose.yml` 不创建 Redis 容器，后端显式
使用 `Redis__Enabled=false`；开发与 CI 测试环境仍保留 Redis，用于继续验证现有 Redis
实现。

暂停原因是当前生产服务器无法稳定访问 Docker Hub，而 production Redis 业务 feature
flags 尚未启用。此次调整只取消生产部署硬依赖，不删除 Redis C# 实现，也不代表放弃
后续 Redis 能力。

当前 Deploy workflow：

- 只要求 production 服务器拉取 ClubHub backend/frontend 的 GHCR 镜像。
- 不再要求或传递 `REDIS_PASSWORD`。
- 部署时会清理服务器 `.env` 中上一版遗留的 Redis 配置项。
- `docker compose up -d --remove-orphans` 会移除旧编排遗留的 Redis 容器，但不会主动删除
  已存在的命名卷；未来恢复前应先确认卷内容与是否需要保留。

重新启用 production Redis 前必须先确定生产服务器可稳定获取的 Redis 镜像来源（例如
受控镜像仓库或可靠 registry mirror），再恢复 Compose service、认证 Secret、readiness
依赖和对应 Deploy 配置。不要只打开 Redis feature flag 而不恢复基础设施。

## 配置基线

- 开发与 CI 测试使用固定镜像 `redis:8.2.8-alpine`；production 当前不部署 Redis 镜像。
- Redis 启用时只连接 Compose 内部网络，不映射宿主机或公网 `6379`。
- 开发数据保存在 `redis-dev-data` 命名卷；production 恢复 Redis 后再使用受控的生产命名卷。
- Redis 启用时 AOF 使用 `appendfsync everysec`，同时每 300 秒且至少发生 10 次写入时生成 RDB。
- `maxmemory-policy` 固定为 `noeviction`。达到上限后写入应失败，不能静默淘汰未来
  的会话、幂等状态或 Stream Pending 消息。
- 开发环境密码通过 `.env` 注入；未来恢复 production Redis 时通过目标 GitHub Environment
  Secret `REDIS_PASSWORD` 注入。仓库、镜像和日志中不得保存真实密码。

## 启动和健康检查

首次本地启动：

```powershell
Copy-Item .env.example .env
# 编辑 .env，将 REDIS_PASSWORD 替换为本机专用随机密码
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps
Invoke-RestMethod http://localhost:5000/health/ready
```

Redis 没有宿主机端口。需要调试时通过容器执行客户端：

```powershell
docker compose -f docker-compose.dev.yml exec redis sh
# 容器内执行；REDIS_PASSWORD 已由 Compose 注入
REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning ping
```

`/health/live` 只检查 API 进程。Redis 启用时，`/health/ready` 同时检查 Redis；Redis 故障时
readiness 会返回 503，防止部署流程误判成功。Redis 总开关关闭时，Redis health check
按设计返回 Healthy（`Redis is disabled.`），因此当前 production readiness 不依赖 Redis。

## 功能启用与回滚顺序

所有 Redis 业务开关默认关闭。production 当前还需要先恢复 Redis 基础设施，之后才能按
以下顺序人工启用：

1. 确认生产服务器能够稳定拉取受控 Redis 镜像，恢复 Compose Redis service、
   `REDIS_PASSWORD`、后端连接配置和 Redis readiness 依赖，并先保持所有业务 feature
   flags 为关闭状态。
2. 备份 Oracle 与 Redis，并在隔离 Schema 验证
   `database/migrations/20260726_add_idempotency_records.sql`。
3. 人工执行迁移后启用 `REDIS_IDEMPOTENCY_ENABLED`，再按需启用权限缓存、预览会话
   和限流。
4. 最后启用 `REDIS_AUTH_SESSIONS_ENABLED`；已有纯签名 Token 会全部失效，需提前通知
   用户重新登录。

认证会话回滚也会要求全员重新登录。权限缓存可关闭并直接回源 Oracle；限流、预览
与幂等在 Redis 故障时不得绕过。若幂等写回 Redis 失败，先保留
`IDEMPOTENCY_RECORDS`，由 Oracle 台账继续重放已提交结果。

## 备份

本节仅适用于实际正在运行 Redis 的环境；production Redis 暂停期间没有可由当前
`docker-compose.yml` 执行的 Redis 备份命令。重新启用 production Redis 后，应在版本升级、
密码轮换和重大功能上线前至少生成一次 RDB，并将文件复制到受控的异机存储。

开发环境示例：

```powershell
$composeFile = 'docker-compose.dev.yml'
$before = [long](docker compose -f $composeFile exec -T redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning LASTSAVE')
docker compose -f $composeFile exec -T redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning BGSAVE'
do {
  Start-Sleep -Seconds 1
  $persistence = docker compose -f $composeFile exec -T redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO persistence'
  $persistenceText = $persistence -join "`n"
  $after = [long](docker compose -f $composeFile exec -T redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning LASTSAVE')
} while (
  $persistenceText -notmatch 'rdb_bgsave_in_progress:0' -or
  $persistenceText -notmatch 'rdb_last_bgsave_status:ok' -or
  $after -le $before
)
docker compose -f $composeFile cp redis:/data/dump.rdb ./redis-backup-dump.rdb
Get-FileHash ./redis-backup-dump.rdb -Algorithm SHA256
```

备份文件可能包含业务状态，必须限制访问权限并按团队数据保留策略清理。不要把备份
提交到 Git。

## 隔离恢复演练

恢复只能在新建的临时卷或明确停机后的目标卷中进行。先记录备份 SHA-256，再创建
隔离 Compose 配置或临时容器，将 `dump.rdb` 放入空 `/data`。不要覆盖正在运行的
生产卷。

1. 校验备份文件哈希并记录 Redis 镜像版本。
2. 用新命名卷启动同版本 Redis，使用新的临时密码。
3. 将 RDB 放入空数据目录后启动 Redis。
4. 使用 `DBSIZE`、抽样 Key 和业务对账确认恢复结果。
5. 验证完成后停止临时容器；经人工确认再删除临时卷。

生产恢复时，先停止写流量和 Redis，保留原卷，只切换到验证通过的新卷。若恢复后
对账失败，立即切回原卷并保持业务入口关闭。

## 升级和回滚

1. 阅读目标版本发布说明，确认 AOF/RDB 兼容性。
2. 生成 RDB 备份并完成隔离恢复演练。
3. 在开发或 staging 将 Compose 镜像改为精确补丁版本，运行构建、健康检查和业务
   故障测试。
4. production Redis 已恢复的情况下，在维护窗口拉取镜像并重新创建 Redis；确认 AOF
   加载、readiness、内存和业务查询均正常。
5. 回滚时恢复原镜像版本和原卷；禁止用空卷覆盖仍可恢复的数据。

production Redis 仍处于暂停状态时，不执行 Redis 升级或轮换操作；应先完成“功能启用与
回滚顺序”中的基础设施恢复步骤。

## 密码轮换

production Redis 恢复后才需要执行以下流程：

1. 生成新的高强度随机密码并更新目标 GitHub Environment 的 `REDIS_PASSWORD`。
2. 在维护窗口重新部署 Redis 和后端，使服务端与客户端同时使用新密码。
3. 验证 `/health/ready`、缓存回源和重建。
4. 确认旧密码无法连接，再移除本地临时记录。

轮换期间普通查询可以回源 Oracle，但依赖 Redis 可靠状态的后续功能必须保持关闭，
直到 readiness 和对账均通过。

## 容量和告警

以下指标只在 Redis 实际启用的环境中监控：

| 指标 | 告警建议 |
| --- | --- |
| `used_memory / maxmemory` | 70% 预警，85% 严重告警 |
| Redis 数据卷磁盘使用率 | 70% 预警，85% 严重告警 |
| `evicted_keys` | 必须始终为 0，任何增长立即告警 |
| `rejected_connections`、连接失败和超时 | 持续增长或连续 5 分钟异常时告警 |
| `aof_last_write_status`、`aof_last_bgrewrite_status` | 非 `ok` 立即告警 |
| `/health/ready` | Redis 启用后连续失败应立即停止部署并通知维护者 |
| 缓存命中、未命中、回源和重建失败 | 回源或失败率突增时检查 Redis 与 Oracle |
| 缓存重建租约 `owner-mismatch` | 持续出现时检查 Oracle 慢查询并调整租约 TTL |
| 会话/限流/预览 503 比例 | 任一持续增长时停止新流量并检查 Redis 连通性、AOF 和容量 |
| `IDEMPOTENCY_RECORDS` 过期清理积压 | 连续两个清理周期增长时检查 Oracle 任务和索引 |

常用只读检查（Redis 已启用环境）：

```powershell
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO memory'
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO persistence'
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO stats'
docker compose logs --tail 100 redis
```

## 故障排查

- **production 找不到 Redis 容器**：先确认当前是否仍处于本手册顶部记录的“production
  Redis 暂停”状态；暂停期间这是预期行为，不应通过临时拉取 Docker Hub 镜像绕过流程。
- **认证失败**：Redis 已启用环境中确认 `.env` 或 GitHub Secret 已更新，并重新创建 Redis
  与后端容器；不要在日志中打印变量值。
- **readiness 503**：Redis 已启用时检查 Redis 容器健康状态、网络、认证和 AOF 加载日志；
  Redis 总开关关闭时应先排查其他 readiness 项。
- **写入失败且内存接近上限**：保持 `noeviction`，先停止产生可靠状态的新入口，分析
  Key、TTL 和 Stream backlog；不得临时改为淘汰策略。
- **AOF/RDB 错误**：停止写流量，保留卷和日志，使用最近备份在隔离卷恢复。
- **Redis 暂时断连**：普通缓存自动回源 Oracle；恢复连接后让缓存自然重建，不执行
  `FLUSHALL`。
