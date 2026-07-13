<!--
=================================================================
PR 标题格式：<type>(可选 scope): <简短中文摘要>
  例如：feat(activity): 新增活动报名人数限制
       fix(venue): 修复场地预约冲突
       docs: 更新 README
  允许的 type 见 CONTRIBUTING.md → Commit 信息

标签指引（创建 PR 后立即添加，均为必选）：
  类型（必选一）  ：课程功能点 / documentation / enhancement / bug
  优先级（必选一） ：优先级:P0 / 优先级:P1 / 优先级:P2
  领域（必选一）  ：area:auth / area:club / area:activity / area:venue /
                    area:project / area:learning / area:material /
                    area:evaluation / area:notice / area:analytics /
                    area:forum / area:frontend / area:recruitment /
                    area:docs / area:meta
  全栈任务       ：如果涉及前后端数据库联动，加上（必选）
=================================================================
-->

## 改动内容

<!-- 简洁说明本次 PR 做了什么 -->

## 改动原因

<!-- 为什么需要这个改动 -->

---

## 关联 Issue

<!--
格式（注意：Closes 必须放在行首，不能缩进或用列表符号）：
  Closes #123   ← 同时关闭 Issue
  Part of #456  ← 部分解决，不关闭
如果不对应 Issue 请填"无"
-->

Closes #

## 审查关注点

<!-- 希望 reviewer 重点审查哪些方面，例如：权限判断是否正确、边界条件是否覆盖、与现有逻辑的兼容性等 -->

## UI 改动

<!-- 涉及前端改动时，请附上截图或录屏对比，可拖入图片到此处 -->

| 改动前 | 改动后 |
|--------|--------|
|        |        |

## 测试说明

<!-- 如何验证本次改动、测试场景、回归范围 -->

---

### 检查清单

**代码质量**
- [ ] 后端代码已构建通过（`dotnet build`）
- [ ] 前端代码已构建通过（`pnpm build`）
- [ ] 已本地手工测试主要场景

**数据库（仅涉及时勾选）**
- [ ] 无表结构变化
- [ ] 表结构变更已写入 `database/`
- [ ] 种子数据、视图、索引、触发器、存储过程已同步

**文档与部署（仅涉及时勾选）**
- [ ] 课程文档已同步更新
- [ ] 需要新增或修改 GitHub Secrets（请在 PR 描述中注明）
- [ ] 需要修改服务器配置或手动迁移
