# Redis 数据边界与故障降级基线

> 适用范围：Issue #151 下的 Redis 子任务 #153–#160
> 基线任务：Issue #152
> 状态：设计基线；#153–#155 已完成基础组件、部署和首批通用缓存接入

## 1. 目的

本文规定 ClubHub 引入 Redis 时的数据边界、Key 规范、生命周期、容量、持久化、
故障行为和回滚方式。后续 Redis 子任务应以本文为默认约束；需要偏离时，必须在
对应 Issue 和 PR 中写明原因、风险及替代方案。

Oracle 是正式业务数据的唯一事实来源。Redis 不替代 Oracle 事务、外键、唯一约束
和审计记录，也不能成为无法重建的业务事实的唯一存储位置。

本轮 Redis 接入不修改 API 契约或数据库结构；Oracle 继续作为唯一事实来源。

## 2. 当前行为

以下内容基于 #153–#155 完成后的当前实现。

| 范围                 | 当前实现                                                                           | 现有保护                                                  | 已知边界                                                                       |
| -------------------- | ---------------------------------------------------------------------------------- | --------------------------------------------------------- | ------------------------------------------------------------------------------ |
| 登录                 | 后端签发 12 小时 HMAC 自签名 Token                                                 | 校验签名和过期时间；登录和会话查询会检查 Oracle 账号状态  | Token 没有会话 ID，无法单独撤销；鉴权处理器本身不会回查账号状态                |
| 权限                 | `AuthService` 从 Oracle 读取账号、系统角色和社团范围角色                           | 每次权限检查重新计算                                      | 高频接口会重复查询；角色变化不需要处理缓存失效                                 |
| 社团、活动、场地查询 | 活动详情和场地详情使用 Cache Aside；社团查询仍直接访问 Oracle                      | 空值缓存、TTL 抖动、跨实例重建租约和 Redis 故障回源       | 活动报名人数及用户报名状态仍实时查询；列表尚未缓存                             |
| 活动报名             | Read Committed 事务内对 `ACTIVITIES` 执行 `FOR UPDATE`，再检查资格、重复报名和容量 | 同一活动的报名被 Oracle 行锁串行化，写入失败最多重试 3 次 | `ACTIVITY_PARTICIPATIONS` 尚无“同一活动、同一用户仅一条有效报名”的业务唯一约束 |
| 场地预约             | 写入和审批前查询已通过预约的重叠时间段                                             | 应用层冲突检查                                            | 检查与写入不在同一个锁定临界区，并发审批可能同时通过                           |
| 学习预览             | 预览元数据存入单进程 `MemoryCache`                                                 | Token、用户、学习项三者必须匹配；默认 30 分钟过期         | 最多 4096 个会话；不能跨实例共享，进程重启后全部丢失                           |
| 通知已读             | 先查 `NOTICE_READS`，没有记录时写 Oracle                                           | `(notice_id, user_id)` 唯一约束吸收并发重复写入           | Redis 未读数以后只能作为投影，不能替代该表                                     |

现有测试已经覆盖预览 Token 隔离、预览会话绑定、通知权限矩阵、通知已读唯一约束
模型和未登录访问等行为。登录完整流程、活动报名并发、场地预约冲突以及主要查询
接口仍需按第 10 节做可重复的手工基线验证。Redis 接入后的自动化和故障注入测试
由 #153–#160 分阶段补齐。

## 3. 总体规则

### 3.1 数据所有权

- Oracle 保存用户、角色、社团成员、活动、报名、场地预约、学习记录、通知已读和
  审计记录等正式业务数据。
- Redis 可以保存 Oracle 数据的副本、短期会话、并发协调状态、尚待落库的 Stream
  消息和可重建读模型。
- 某项实时能力如果无法从 Oracle 或可靠业务事件重建，不得上线为正式功能。
- 不在 Redis 值、Key 或日志中保存密码、原始 Token、验证码、完整连接串和内部
  拓扑。需要以 Token 或客户端幂等键定位时，使用 SHA-256 摘要。

### 3.2 Key 格式

所有 Key 由统一构造器生成，Controller 不得自行拼接：

```text
clubhub:{environment}:{module}:{purpose}:v1:{identity}
```

- `environment` 使用 `dev`、`test`、`staging`、`prod` 等部署环境名，禁止不同环境
  共用无隔离前缀的 Key。
- `module` 使用稳定的小写模块名，例如 `auth`、`club`、`activity`、`venue`、
  `learning`、`notice`。
- `purpose` 表示用途，例如 `detail`、`session`、`lock`、`idempotency`、`stream`。
- `v1` 是载荷版本。发生不兼容变更时写入新版本并让旧版本自然过期，不原地误读。
- `identity` 只使用数据库 ID、规范化查询摘要或敏感值的 SHA-256 摘要。
- Key 总长度不超过 200 字节；动态文本先规范化并做摘要，禁止把标题、姓名、文件名
  或请求正文直接放入 Key。

示例：

```text
clubhub:prod:club:detail:v1:42
clubhub:prod:auth:session:v1:sha256-hex
clubhub:prod:venue:lock:v1:18:2026-07-26
clubhub:prod:activity:registration-stream:v1
```

### 3.3 序列化和时间

- 业务载荷统一使用 `System.Text.Json`，保存 `schemaVersion` 和必要的生成时间。
- 时间一律保存 UTC 时间戳；展示时再转换为业务时区。
- 缓存单值序列化后不得超过 256 KiB，幂等响应不得超过 64 KiB。超过限制时跳过
  Redis，不拆成大量不可追踪的 Key。
- 列表缓存必须分页。默认每页不超过 50 条，接口允许更大页时仍不得超过其 OpenAPI
  契约上限。

## 4. 数据分类与生命周期

表中的 TTL 是初始默认值。后续实现可以在对应 Issue 中调小，但延长持久状态的 TTL
必须说明容量影响。

### 4.1 可重建缓存

| 数据             | Key / 类型                                       |                 TTL | 容量边界                     | 权威来源与重建                                            |
| ---------------- | ------------------------------------------------ | ------------------: | ---------------------------- | --------------------------------------------------------- |
| 社团公开详情     | `club:detail:v1:{clubId}` / String(JSON)         |  10 分钟，±20% 抖动 | 单值 256 KiB                 | Oracle `CLUBS` 及公开关联数据；未命中时回源               |
| 活动公开详情     | `activity:detail:v1:{activityId}` / String(JSON) |   2 分钟，±20% 抖动 | 单值 256 KiB                 | Oracle `ACTIVITIES`；报名人数按一致性要求单独读取或短缓存 |
| 场地详情         | `venue:detail:v1:{venueId}` / String(JSON)       |   5 分钟，±20% 抖动 | 单值 256 KiB                 | Oracle `VENUES`                                           |
| 已占用时间段页   | `venue:occupied:v1:{queryHash}` / String(JSON)   |   1 分钟，±20% 抖动 | 每页不超过接口上限           | Oracle `VENUE_RESERVATIONS`                               |
| 学习项公开元数据 | `learning:item:v1:{itemId}` / String(JSON)       |   5 分钟，±20% 抖动 | 不缓存文件内容；单值 256 KiB | Oracle `LEARNING_ITEMS` 和对象存储元数据                  |
| 列表页           | `{module}:list:v1:{queryHash}` / String(JSON)    | 1–2 分钟，±20% 抖动 | 必须分页；不得缓存无上限全集 | 对应 Oracle 查询                                          |
| 不存在实体       | 原详情 Key，使用带类型的空值载荷                 |    30 秒，±10% 抖动 | 仅允许合法 ID 或规范查询产生 | Oracle 返回不存在后写入；写操作成功后删除                 |

采用 Cache Aside：先读 Redis，未命中时回源 Oracle，再尝试写入缓存。Oracle 写事务
成功后删除相关详情和列表 Key；事务提交前不得删缓存。删除失败时记录告警，依靠短
TTL 收敛。Redis 不可用或缓存写入失败时，直接返回 Oracle 结果。

热点重建使用短租约互斥锁或逻辑过期。未取得重建权的请求可以在数据允许时回源
Oracle，但不得无限等待 Redis。

### 4.2 短期会话与权限

| 数据         | Key / 类型                                               |                                                   TTL | 容量边界                                           | 持久化与权威来源                               |
| ------------ | -------------------------------------------------------- | ----------------------------------------------------: | -------------------------------------------------- | ---------------------------------------------- |
| 登录会话     | `auth:session:v1:{tokenHash}` / Hash 或 String(JSON)     |               不超过 Token 剩余寿命，当前上限 12 小时 | 每个有效 Token 一条；限制单账号并发会话数，默认 10 | AOF；账号状态和身份仍以 Oracle 为准            |
| 权限快照     | `auth:permissions:v1:{userId}` / String(JSON)            |                                                5 分钟 | 每用户一份，单值 64 KiB                            | 可重建缓存；从 Oracle 用户、角色、成员任期重建 |
| 学习预览会话 | `learning:preview-session:v1:{tokenHash}` / String(JSON) | 与预览 Cookie 相同，默认 30 分钟，配置范围 1–120 分钟 | 全系统默认最多 4096 条；单值 32 KiB                | AOF；访问权限每次仍回查 Oracle                 |

登录会话使用 allowlist 语义：签名有效并且 Redis 中存在对应会话，Token 才有效。
注销、账号停用或管理员强制下线时删除会话。后续引入会话 ID 时，API 响应结构可以
保持不变，但实现必须先经过 #156 的设计和测试。

角色分配、成员任期或职位变化、账号状态变化后，主动删除对应权限快照。缓存未命中
或读取失败时回源 Oracle；权限检查不得因为 Redis 出错而默认放行。

预览会话只保存已准备资源的元数据，不保存文件正文。达到 4096 条上限时停止创建新
会话并返回 503，不得通过 Redis 淘汰策略随机删除仍有效会话。

### 4.3 锁、限流与幂等

| 数据     | Key / 类型                                                       |                                          TTL | 容量边界                                  | 持久化与权威来源                                   |
| -------- | ---------------------------------------------------------------- | -------------------------------------------: | ----------------------------------------- | -------------------------------------------------- |
| 分布式锁 | `{module}:lock:v1:{resource}` / String(owner)                    | 按业务设置，通常 10–30 秒；Office 转换可更长 | 每个共享资源最多一条                      | 不依赖持久化；锁过期后必须在 Oracle 事务内复查条件 |
| 限流窗口 | `{module}:rate-limit:v1:{subjectHash}:{window}` / String 或 ZSet |                           窗口长度加短暂余量 | 每个主体仅保留活动窗口；限制 ZSet 元素数  | 短期状态；策略配置是权威来源                       |
| 幂等状态 | `{module}:idempotency:v1:{keyHash}` / Hash                       |                                 默认 24 小时 | 响应不超过 64 KiB；单用户活动请求数设上限 | AOF；Oracle 唯一约束或幂等台账是最终兜底           |

锁使用随机 owner，获取操作必须是原子 `SET NX PX`，释放时用 Lua 比较 owner 后再
删除。禁止无 TTL 锁和无限重试。锁失效只表示可以重新竞争，不表示业务条件仍成立；
取得锁后仍要开启 Oracle 事务并重新检查。

幂等状态至少区分 `processing`、`succeeded` 和可重试失败。相同幂等键和相同请求
摘要可以复用结果；同一幂等键对应不同请求摘要时返回冲突。Redis 不可用时，已声明
必须幂等的写接口安全拒绝，不能绕过保护直接写 Oracle。

登录失败、注册、签到码或签退码校验、预览转换等敏感限流在 Redis 不可用时默认
返回 503。若后续希望提供降级配额，必须证明多实例下不会形成默认放行。

### 4.4 Stream 消息

活动报名 Stream 的初始命名如下：

```text
clubhub:{environment}:activity:registration-stream:v1
clubhub:{environment}:activity:registration-request:v1:{requestId}
clubhub:{environment}:activity:registration-dead-letter:v1
```

| 项目         | 基线                                                                     |
| ------------ | ------------------------------------------------------------------------ |
| 投递语义     | 单消费组、至少一次投递                                                   |
| ACK 时机     | Oracle 事务成功提交并写入幂等结果后                                      |
| Pending 恢复 | 监控 `XPENDING`，超过空闲阈值后由健康消费者 `XAUTOCLAIM` 接管            |
| 请求状态 TTL | 处理中不设短 TTL；终态 24 小时                                           |
| 失败记录     | 进入死信 Stream，保留 7 天并提供人工重放流程                             |
| 容量         | 正常 Stream backlog 硬上限 100,000 条；死信上限 10,000 条                |
| 裁剪         | 只裁剪已 ACK 且超过保留期的消息，不得让生产者盲目裁剪仍在 Pending 的记录 |
| 持久化       | AOF `everysec`，配合命名卷和定期 RDB 备份                                |
| 对账         | 定期比较 Redis 剩余名额、报名用户集合和 Oracle 有效报名记录              |

达到 backlog 或死信上限、Redis 只读、写满、断连或超时时，报名入口停止接收并返回
503。不得自动调用旧的 Oracle 直接报名路径。恢复后先检查 Pending、死信和对账结果，
确认一致再开放入口。

### 4.5 实时读模型与统计

| 数据             | Key / 类型                                        |     TTL / 保留期 | 容量边界                              | 权威来源与重建                           |
| ---------------- | ------------------------------------------------- | ---------------: | ------------------------------------- | ---------------------------------------- |
| 活动或社团排行榜 | `analytics:ranking:v1:{scope}:{period}` / ZSet    | 当前周期加 30 天 | 每榜最多 10,000 项                    | Oracle 活动、参与、学习或考核记录        |
| 用户通知 Feed    | `notice:feed:v1:{userId}` / ZSet                  |            30 天 | 每用户最多 1,000 项                   | Oracle `NOTICES` 和目标范围              |
| 未读数           | `notice:unread:v1:{userId}` / String              | 7 天，访问时续期 | 每用户一条                            | `NOTICES` 与 `NOTICE_READS` 重新计算     |
| 学习打卡         | `learning:checkin:v1:{userId}:{year}` / Bitmap    |           400 天 | 位偏移使用当年日期序号，不使用用户 ID | 只有 Oracle 存在每日学习事实后才允许启用 |
| 日 PV            | `analytics:pv:v1:{resource}:{date}` / String      |            90 天 | 每资源每天一条                        | 演示统计，可丢失                         |
| 日 UV            | `analytics:uv:v1:{resource}:{date}` / HyperLogLog |            90 天 | 每资源每天一条                        | 演示统计，是近似值且可丢失               |

Feed 分页使用“分值 + 唯一 ID”游标，避免同一时间戳下漏项。正式已读、考核和学习
打卡事实不能只存在 Redis。当前 `LEARNING_RECORDS` 只有最近学习时间等字段，不能
完整重建每日打卡历史，因此 Bitmap 功能必须等 Oracle 事实模型确认后再实施。

## 5. Redis 实例、持久化和内存

项目初期使用一个 Redis 实例，降低课程项目的部署复杂度。该实例同时承载缓存、
会话、锁、幂等和 Stream，因此采用以下保守配置：

- 固定 Redis 版本或镜像 digest，生产环境不向公网映射 6379。
- 设置明确的 `maxmemory`，策略使用 `noeviction`。Redis 写满后拒绝新增数据，仍可
  读取已有 Key，避免会话、幂等状态或 Pending 消息被静默淘汰。
- 启用 AOF，`appendfsync everysec`；使用命名卷并定期生成、异机保存 RDB 备份。
- 缓存通过短 TTL、抖动、分页和单值限制控制容量；Stream 通过 admission control
  和 backlog 告警控制容量。
- 当可靠状态和普通缓存的容量相互影响已不可接受时，优先拆分缓存实例与可靠状态
  实例，再为缓存实例评估 `allkeys-lfu`。拆分前不得直接改变当前实例的淘汰策略。

Redis 官方文档说明，AOF `everysec` 在故障时仍可能丢失最近约一秒的数据。因此
Oracle 幂等约束、消费对账和补偿不能省略。RDB 适合备份和快速恢复，但单独使用时
可能丢失最近数分钟的数据。

## 6. 故障矩阵

| 数据或能力             | 断连 / 超时                               | Redis 重启                             | 内存写满 / 只读                        | 恢复动作                             |
| ---------------------- | ----------------------------------------- | -------------------------------------- | -------------------------------------- | ------------------------------------ |
| 普通查询缓存           | 立即回源 Oracle；本次不写缓存             | 回源并自然预热                         | 返回 Oracle 结果，缓存写失败只记指标   | 检查连接恢复，允许自然重建           |
| 权限快照               | 回源 Oracle，不得默认放行                 | 回源重建                               | 回源 Oracle；失效通知写失败需告警      | 主动清理受影响用户 Key 或等待 TTL    |
| 登录会话               | 返回 503，不把“无法验证”伪装成 Token 无效 | AOF 恢复前保持 503                     | 已有会话可读；无法创建或续期时返回 503 | 校验 AOF、会话数量和账号状态         |
| 学习预览会话           | 创建和读取均返回 503，提示重新打开预览    | AOF 恢复；元数据指向的文件仍需再次校验 | 停止创建新会话                         | 校验对象存储和预览文件后恢复         |
| 限流                   | 敏感入口返回 503                          | 窗口状态由 AOF 恢复或自然过期          | 停止受保护请求                         | 确认计数窗口正常后开放               |
| 幂等                   | 必须幂等的写操作返回 503                  | 从 AOF 恢复；同时核对 Oracle           | 拒绝新请求，不清理旧状态腾空间         | 对账 `processing` 状态和 Oracle 结果 |
| 分布式锁               | 无法取得或确认锁时拒绝关键操作            | 等遗留锁 TTL 到期，不信任旧 owner      | 拒绝新锁                               | Oracle 事务内重新检查业务条件        |
| Stream 报名            | 暂停入口，禁止走旧路径                    | 先恢复 Pending 和消费者，再开放        | 达到阈值前主动停入口                   | 接管 Pending、处理死信、执行对账     |
| 排行榜 / Feed / 未读数 | 能回源的回源 Oracle，否则暂时隐藏         | 从 Oracle 重建                         | 停止更新投影，正式写入照常进入 Oracle  | 全量或增量重建后重新展示             |
| PV / UV                | 丢弃本次统计并记录指标                    | 接受少量丢失                           | 丢弃本次统计                           | 无需反写业务库                       |

所有降级日志只记录功能名、操作类型、错误分类、耗时和关联请求 ID。不得记录 Redis
密码、完整地址、原始 Key、Token、验证码或请求正文。

## 7. 配置开关与回滚

后续基础组件采用强类型内部配置，默认关闭所有 Redis 业务能力：

```text
Redis:Enabled
Redis:Features:Cache
Redis:Features:AuthSessions
Redis:Features:PermissionCache
Redis:Features:RateLimiting
Redis:Features:Idempotency
Redis:Features:DistributedLocks
Redis:Features:RealtimeReadModels
ActivityRegistrationMode = OracleDirect | RedisStream
```

`ActivityRegistrationMode` 必须是单值枚举，不能拆成两个可能同时开启的布尔开关。
启动时遇到未知值或冲突配置应直接失败。连接串、用户名和密码只从环境变量、Secret
或本机忽略配置注入。

回滚边界如下：

- 普通缓存和权限缓存可以立即关闭，读请求回到 Oracle。
- 实时读模型可以关闭展示或改为 Oracle 查询，不删除 Redis 数据。
- AuthSessions 上线后，回滚到纯签名 Token 会失去撤销能力。回滚必须使现有会话
  失效、要求用户重新登录，不能同时接受两套会话语义。
- 分布式锁只能在恢复了安全的 Oracle 串行化方案或确认单实例维护窗口后关闭。
  不得在请求中自动尝试“Redis 锁失败后走无锁旧路径”。
- RedisStream 报名回滚顺序固定为：关闭报名入口、停止生产新消息、排空或冻结
  Pending、完成 Oracle 对账、切换为 `OracleDirect`、重新开放入口。
- 回滚不删除生产卷、Stream、Pending、死信或幂等状态。清理由独立、可审计的维护
  操作完成。

## 8. Oracle 候选约束与索引

本节只记录后续 Issue 需要确认的数据库改进，不授权直接修改表结构。

| 候选项                                                  | 目的                                               | 后续处理                                                  |
| ------------------------------------------------------- | -------------------------------------------------- | --------------------------------------------------------- |
| `ACTIVITY_PARTICIPATIONS` 活动 + 用户的有效报名唯一约束 | 保证 Stream 重复消费或并发请求不会生成两条有效报名 | #158 先检查历史重复数据，再评估函数索引或显式有效状态字段 |
| 活动报名状态计数索引                                    | 加速按活动和有效状态统计人数                       | #158 根据实际 SQL 和执行计划确定列顺序                    |
| Stream 请求 ID 的 Oracle 唯一约束或 inbox/幂等台账      | 给至少一次消费提供最终幂等边界                     | #158 先确认是否新增列或独立表，再提供增量迁移             |
| `VENUE_RESERVATIONS` 场地、状态、开始和结束时间索引     | 加速时间重叠查询                                   | #157 根据 Oracle 执行计划确认；索引不能替代锁内复查       |
| `CLUB_MEMBERS` 社团、用户、成员状态索引                 | 加速报名资格和权限范围查询                         | #155/#158 用实际查询验证收益                              |

场地预约主键已由 `SEQ_VENUE_RESERVATIONS` 生成，不再使用 `MAX(id)+1`。时间区间重叠
无法靠普通唯一约束完整表达，仍需“按场地和日期取得分布式锁、开启 Oracle 事务、
锁内重新查询冲突”的组合保护。

现有约束无需重复新增：

- `NOTICE_READS(notice_id, user_id)` 已有唯一约束，通知已读投影以它为准。
- `USER_ROLES` 已有覆盖全局和社团范围的唯一索引，权限缓存失效不能替代该约束。

任何数据库调整都必须另开带 `migration` 标签的任务或使用已明确包含迁移的后续
Issue，先取得用户确认，再同步 `schema.sql`、增量迁移、`verify.sql` 和数据库文档。

## 9. 后续 Issue 使用索引

| Issue                       | 必须遵守的章节                                   |
| --------------------------- | ------------------------------------------------ |
| #153 基础组件               | 第 3 节 Key、序列化和载荷限制；第 6 节错误分类   |
| #154 部署配置               | 第 5 节 `noeviction`、AOF、RDB、网络和容量策略   |
| #155 通用缓存               | 第 4.1 节 Cache Aside、TTL、空值、抖动和失效规则 |
| #156 会话、权限、限流、幂等 | 第 4.2、4.3、6、7 节                             |
| #157 分布式锁               | 第 4.3、6、7、8 节                               |
| #158 高并发报名             | 第 4.4、6、7、8 节；不得双写                     |
| #159 实时数据               | 第 4.5 节；正式事实不得只存 Redis                |
| #160 测试与交付             | 第 6、7、10 节及全部容量、恢复假设               |

## 10. 当前基线验证

### 10.1 自动化测试

在仓库根目录执行：

```powershell
dotnet test ClubHub.sln --configuration Release --no-restore
```

测试必须使用 `backend.Tests` 的内存测试数据库。Oracle 专属集成测试只能连接隔离
Schema 或一次性数据库，不能连接团队共享 Oracle。当前已知的
`Microsoft.OpenApi 2.0.0` 安全告警和生成模型 nullable 告警不属于 #152 的修改
范围，但不得把新的失败归因于这些既有告警。

本基线在 2026-07-26、`dev` 提交 `8cc3a935` 上执行上述命令：93 个测试通过，
0 失败，0 跳过。后续子任务应记录各自分支的新结果，不把测试总数固定为验收条件。

### 10.2 手工验证环境

只在本地开发库或明确的隔离测试库执行。准备两个正常账号、一个已停用账号、一个
有余量的已发布活动、一个已满活动、一个可预约场地、一个可预览学习项和一条已发布
通知。不要在命令、截图或日志中保存真实密码和 Token。

以下示例使用 PowerShell，变量内容只保存在当前终端：

```powershell
$apiBase = "http://localhost:5000/api"
$loginBody = @{
  username = $env:CLUBHUB_TEST_USERNAME
  password = $env:CLUBHUB_TEST_PASSWORD
} | ConvertTo-Json
$login = Invoke-RestMethod `
  -Method Post `
  -Uri "$apiBase/auth/login" `
  -ContentType "application/json" `
  -Body $loginBody
$headers = @{ Authorization = "Bearer $($login.token)" }
```

### 10.3 可重复场景

| 场景           | 操作                                                                                        | 当前预期                                                     |
| -------------- | ------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| 登录成功       | 使用正常账号调用 `POST /auth/login`，再调用 `GET /auth/session`                             | 返回 Token、用户、角色和权限；Token 最长 12 小时             |
| 登录失败与停用 | 分别使用错误密码和停用账号登录                                                              | 错误密码返回 401；停用账号返回 403                           |
| 权限           | 用有权限和无权限账号调用 `GET /auth/permissions/check`                                      | 结果来自 Oracle；无权限账号不得放行                          |
| 公开查询       | 连续两次调用 `GET /clubs`、`GET /activities`、`GET /venues`、`GET /learning/items`          | 两次都直接查询 Oracle，数据一致                              |
| 重复活动报名   | 同一账号连续两次调用 `POST /activities/{id}/registrations`                                  | 第一次创建报名；第二次返回 409 `ALREADY_REGISTERED`          |
| 活动容量       | 对已满活动报名                                                                              | 返回 409 `CAPACITY_FULL`，Oracle 不新增有效报名              |
| 报名并发       | 在隔离库中让多个合格账号并发报名接近满额的活动                                              | 依靠活动行锁不超额；失败请求返回冲突，不产生重复有效报名     |
| 场地冲突       | 创建或审批时间重叠的预约                                                                    | 已有已通过预约时返回 409；记录当前实现尚无锁内复查的并发风险 |
| 学习预览       | 调用 `POST /learning/items/{id}/preview-session` 保存 Cookie，再携带 Cookie 请求 `/preview` | 会话有效时返回预览；错用户、错学习项或过期会话返回未授权     |
| 通知已读       | 同一账号两次调用 `POST /notices/{id}/reads`                                                 | 两次都返回已读；Oracle 只有一条 `(notice_id, user_id)` 记录  |

并发报名和场地冲突验证结束后清理隔离测试数据。不得对生产或演示数据库执行清理、
压测或故障注入。

## 11. 参考资料

- [Redis persistence](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/)
- [Redis key eviction](https://redis.io/docs/latest/develop/reference/eviction/)
- [Redis Streams](https://redis.io/docs/latest/develop/data-types/streams/)
- [Redis streaming use cases](https://redis.io/docs/latest/develop/use-cases/streaming/)
- [StackExchange.Redis configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration.html)
