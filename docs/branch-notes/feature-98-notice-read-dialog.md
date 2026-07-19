# feature/98-notice-read-dialog

关联 Issue: #98

本分支优化通知列表的已读交互，并通过页面内弹窗完整展示通知正文与发布信息。

后续测试补充：通知页统一使用带 Bearer 令牌的请求封装，修复打开发布弹窗时成员接口误报登录失效；同时补齐通知草稿的保存、查看、编辑、发布和删除闭环，并禁止草稿产生已读记录。

审查修正：通知查询、创建、草稿编辑/删除和已读接口统一从 Bearer Token 的 JWT claims 获取当前用户，不再接受客户端传入的身份 ID；草稿写入以保存时间作为并发版本戳，冲突时返回 409，防止并发发布、编辑或删除互相覆盖。

并发幂等补充：`NOTICE_READS` 使用数据库序列生成主键，并对 `(NOTICE_ID, USER_ID)` 增加唯一约束；重复并发写入会返回数据库中的既有已读结果。部署前需在停写窗口执行 `database/migrations/20260717_add_notice_read_idempotency.sql`，随后运行 `database/verify.sql`。
