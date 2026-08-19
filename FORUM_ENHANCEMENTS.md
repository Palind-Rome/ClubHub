# 论坛功能增强进度跟踪

Issue: #174  
Feature Branch: `feature/174-forum-enhancements`

## 实现清单

### 1. Markdown 支持与渲染
- [ ] 后端：修改 FORUM_POSTS 表模型支持 markdown 内容存储
- [ ] 后端：在 ForumPostsController 中添加 markdown 处理逻辑
- [ ] 前端：选择并集成 Markdown 编辑器库
- [ ] 前端：话题/回复编辑页面集成编辑器
- [ ] 前端：话题/回复展示页面集成渲染器
- [ ] API：更新 openapi.yaml 中的 ForumPost 模型

### 2. 图片上传到 OSS
- [ ] 后端：创建 ForumImageUploadService（参考 AwardObjectStorage）
- [ ] 后端：在 ForumPostsController 添加上传端点 `POST /api/v1/forum/upload-image`
- [ ] 前端：在编辑器中集成上传按钮
- [ ] API：在 openapi.yaml 中定义上传接口
- [ ] 数据库：如需要，添加 FORUM_IMAGES 表记录关联关系

### 3. 无限嵌套回复
- [ ] 数据库：修改 FORUM_REPLIES 表，添加 parent_reply_id 字段
- [ ] 数据库：添加迁移脚本
- [ ] 后端：修改 ForumReply 实体模型
- [ ] 后端：修改获取回复逻辑以支持树形结构
- [ ] 前端：修改回复列表展示，支持树形结构缩进
- [ ] 前端：修改回复表单，支持对某条回复进行回复

## 开发步骤

1. 确认数据库变更（FORUM_REPLIES 新增 parent_reply_id）
2. API 定义（openapi.yaml）
3. 后端代码生成 + 实现
4. 前端代码生成 + 实现
5. 单元测试 + 集成测试
6. 代码审查 + 合并
