# Redis 运维手册

本文适用于 ClubHub 开发、演示和生产环境中的单实例 Redis。Oracle 仍是正式业务
数据的唯一事实来源；Redis 中的普通缓存可以自然重建，未来的会话、幂等状态和
Stream 则依赖 AOF、备份和业务对账。

## 配置基线

- 镜像固定为 `redis:8.2.8-alpine`。
- Redis 只连接 Compose 内部网络，不映射宿主机或公网 `6379`。
- 数据保存在 `redis-dev-data`（开发）或 `redis-data`（生产）命名卷。
- AOF 使用 `appendfsync everysec`，同时每 300 秒且至少发生 10 次写入时生成 RDB。
- `maxmemory-policy` 固定为 `noeviction`。达到上限后写入应失败，不能静默淘汰未来
  的会话、幂等状态或 Stream Pending 消息。
- 密码通过 `.env` 或 GitHub Secret `REDIS_PASSWORD` 注入；仓库、镜像和日志中
  不得保存真实密码。

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

`/health/live` 只检查 API 进程；`/health/ready` 同时检查启用的 Redis。Redis 故障时
普通缓存查询应回源 Oracle，但 readiness 会返回 503，防止部署流程误判成功。

## 备份

至少在版本升级、密码轮换和重大功能上线前生成一次 RDB，并将文件复制到受控的
异机存储。下面的命令不会打印密码：

```powershell
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning BGSAVE'
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning LASTSAVE'
docker compose cp redis:/data/dump.rdb ./redis-backup-dump.rdb
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
4. 生产维护窗口内拉取镜像并重新创建 Redis；确认 AOF 加载、readiness、内存和
   业务查询均正常。
5. 回滚时恢复原镜像版本和原卷；禁止用空卷覆盖仍可恢复的数据。

## 密码轮换

1. 生成新的高强度随机密码并更新目标 GitHub Environment 的 `REDIS_PASSWORD`。
2. 在维护窗口重新部署 Redis 和后端，使服务端与客户端同时使用新密码。
3. 验证 `/health/ready`、缓存回源和重建。
4. 确认旧密码无法连接，再移除本地临时记录。

轮换期间普通查询可以回源 Oracle，但依赖 Redis 可靠状态的后续功能必须保持关闭，
直到 readiness 和对账均通过。

## 容量和告警

至少监控以下项目：

| 指标 | 告警建议 |
| --- | --- |
| `used_memory / maxmemory` | 70% 预警，85% 严重告警 |
| Redis 数据卷磁盘使用率 | 70% 预警，85% 严重告警 |
| `evicted_keys` | 必须始终为 0，任何增长立即告警 |
| `rejected_connections`、连接失败和超时 | 持续增长或连续 5 分钟异常时告警 |
| `aof_last_write_status`、`aof_last_bgrewrite_status` | 非 `ok` 立即告警 |
| `/health/ready` | 连续失败立即停止部署并通知维护者 |
| 缓存命中、未命中、回源和重建失败 | 回源或失败率突增时检查 Redis 与 Oracle |

常用只读检查：

```powershell
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO memory'
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO persistence'
docker compose exec redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --no-auth-warning INFO stats'
docker compose logs --tail 100 redis
```

## 故障排查

- **认证失败**：确认 `.env` 或 GitHub Secret 已更新，并重新创建 Redis 与后端容器；
  不要在日志中打印变量值。
- **readiness 503**：检查 Redis 容器健康状态、网络、认证和 AOF 加载日志。
- **写入失败且内存接近上限**：保持 `noeviction`，先停止产生可靠状态的新入口，分析
  Key、TTL 和 Stream backlog；不得临时改为淘汰策略。
- **AOF/RDB 错误**：停止写流量，保留卷和日志，使用最近备份在隔离卷恢复。
- **Redis 暂时断连**：普通缓存自动回源 Oracle；恢复连接后让缓存自然重建，不执行
  `FLUSHALL`。
