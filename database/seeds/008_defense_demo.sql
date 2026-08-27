-- 答辩演示数据：仅用于本地开发库或明确的演示库。
-- 依赖 000_sample_users.sql、001_sample_clubs.sql 和 005_sample_member_terms.sql。
-- 脚本先规范化可明确识别的占位文本、空关联和过期状态，再按 8101 号保留记录
-- 执行 MERGE；不会删除现场录入的数据，可安全重复执行。

UPDATE ACTIVITIES
SET title = CASE
      WHEN TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试', '驳回')
        THEN '历史活动记录 ' || activity_id
      ELSE title
    END,
    location = CASE
      WHEN TRIM(LOWER(location)) IN ('1', 'aaa', 'test', '测试')
        THEN '场地待确认'
      ELSE location
    END,
    activity_status = CASE
      WHEN activity_status IN ('published', 'ongoing') AND end_at < SYSDATE
        THEN 'finished'
      ELSE activity_status
    END
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试', '驳回')
   OR TRIM(LOWER(location)) IN ('1', 'aaa', 'test', '测试')
   OR (activity_status IN ('published', 'ongoing') AND end_at < SYSDATE);

UPDATE PROJECTS
SET project_name = CASE
      WHEN TRIM(LOWER(project_name)) IN ('1', 'aaa', 'zzzz', 'test', '测试')
        THEN '历史协作项目 ' || project_id
      ELSE project_name
    END,
    club_id = CASE
      WHEN club_id IS NULL OR NOT EXISTS (SELECT 1 FROM clubs WHERE clubs.club_id = projects.club_id)
        THEN 1
      ELSE club_id
    END,
    leader_user_id = CASE
      WHEN leader_user_id IS NULL
        OR NOT EXISTS (SELECT 1 FROM users WHERE users.user_id = projects.leader_user_id)
        THEN 3
      ELSE leader_user_id
    END
WHERE TRIM(LOWER(project_name)) IN ('1', 'aaa', 'zzzz', 'test', '测试')
   OR club_id IS NULL
   OR leader_user_id IS NULL
   OR NOT EXISTS (SELECT 1 FROM clubs WHERE clubs.club_id = projects.club_id)
   OR NOT EXISTS (SELECT 1 FROM users WHERE users.user_id = projects.leader_user_id);

UPDATE NOTICES
SET title = '历史通知记录 ' || notice_id,
    content = NVL(content, TO_CLOB('该记录已在答辩前完成内容补全。'))
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试')
   OR content IS NULL;

UPDATE LEARNING_ITEMS
SET title = '历史学习资料 ' || item_id,
    description = NVL(description, TO_CLOB('该资料已在答辩前完成说明补全。'))
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试')
   OR description IS NULL;

UPDATE FORUM_POSTS
SET title = '历史讨论话题 ' || post_id
WHERE parent_post_id IS NULL
  AND (TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试') OR title IS NULL);

MERGE INTO ACTIVITIES target
USING (
  SELECT 8101 AS activity_id, 1 AS club_id, 3 AS creator_user_id,
         '开源项目协作实践营' AS title, 'workshop' AS activity_type,
         '围绕真实开源议题完成组队、任务拆分、代码评审和成果汇报。' AS description,
         '创新创业中心 302' AS location,
         SYSDATE + 2 AS start_at, SYSDATE + 2 + 3 / 24 AS end_at,
         40 AS capacity, 'published' AS activity_status
  FROM dual
) source
ON (target.activity_id = source.activity_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.creator_user_id = source.creator_user_id,
  target.title = source.title,
  target.activity_type = source.activity_type,
  target.description = source.description,
  target.location = source.location,
  target.start_at = source.start_at,
  target.end_at = source.end_at,
  target.capacity = source.capacity,
  target.activity_status = source.activity_status
WHEN NOT MATCHED THEN INSERT (
  activity_id, club_id, creator_user_id, title, activity_type, description,
  location, start_at, end_at, capacity, activity_status, created_at
) VALUES (
  source.activity_id, source.club_id, source.creator_user_id, source.title,
  source.activity_type, source.description, source.location, source.start_at,
  source.end_at, source.capacity, source.activity_status, SYSDATE
);

MERGE INTO PROJECTS target
USING (
  SELECT 8101 AS project_id, 1 AS club_id,
         'ClubHub 校园社团数字化共建' AS project_name,
         '以活动、通知、项目和学习业务闭环为范围，完成系统迭代与答辩交付。' AS description,
         3 AS leader_user_id, SYSDATE - 7 AS start_date, SYSDATE + 30 AS end_date,
         'running' AS project_status, 2 AS reviewer_user_id,
         '立项目标清晰，同意进入执行阶段。' AS review_comment
  FROM dual
) source
ON (target.project_id = source.project_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.project_name = source.project_name,
  target.description = source.description,
  target.leader_user_id = source.leader_user_id,
  target.start_date = source.start_date,
  target.end_date = source.end_date,
  target.project_status = source.project_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment
WHEN NOT MATCHED THEN INSERT (
  project_id, club_id, project_name, description, leader_user_id, start_date,
  end_date, project_status, reviewer_user_id, review_comment, created_at
) VALUES (
  source.project_id, source.club_id, source.project_name, source.description,
  source.leader_user_id, source.start_date, source.end_date, source.project_status,
  source.reviewer_user_id, source.review_comment, SYSDATE
);

MERGE INTO LEARNING_ITEMS target
USING (
  SELECT 8101 AS item_id, 1 AS club_id, 3 AS uploader_user_id,
         CAST(NULL AS NUMBER) AS teacher_user_id,
         '数据库答辩：业务闭环与多表关联说明' AS title,
         'resource' AS item_type, '答辩资料' AS category_name,
         '结合 ClubHub 表关系讲解范式、连接查询、触发器与核心业务状态流转。' AS description,
         '/demo/database-defense-guide.pdf' AS file_url,
         CAST(NULL AS DATE) AS start_at, CAST(NULL AS DATE) AS end_at,
         CAST(NULL AS NUMBER) AS capacity, 'club' AS visibility,
         'member' AS download_permission, 'published' AS item_status
  FROM dual
) source
ON (target.item_id = source.item_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.uploader_user_id = source.uploader_user_id,
  target.title = source.title,
  target.item_type = source.item_type,
  target.category_name = source.category_name,
  target.description = source.description,
  target.file_url = source.file_url,
  target.visibility = source.visibility,
  target.download_permission = source.download_permission,
  target.item_status = source.item_status
WHEN NOT MATCHED THEN INSERT (
  item_id, club_id, uploader_user_id, teacher_user_id, title, item_type,
  category_name, description, file_url, start_at, end_at, capacity, visibility,
  download_permission, item_status, created_at
) VALUES (
  source.item_id, source.club_id, source.uploader_user_id, source.teacher_user_id,
  source.title, source.item_type, source.category_name, source.description,
  source.file_url, source.start_at, source.end_at, source.capacity, source.visibility,
  source.download_permission, source.item_status, SYSDATE
);

MERGE INTO NOTICES target
USING (
  SELECT 8101 AS notice_id, 1 AS club_id, 3 AS publisher_user_id,
         'event' AS notice_type, '开源实践营行前提醒' AS title,
         '请参与成员提前安装开发环境，并于活动开始前十五分钟完成现场签到。' AS content,
         'club' AS target_type, 1 AS target_id,
         SYSDATE - 1 / 24 AS publish_at, SYSDATE + 7 AS expire_at,
         'published' AS notice_status
  FROM dual
) source
ON (target.notice_id = source.notice_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.publisher_user_id = source.publisher_user_id,
  target.notice_type = source.notice_type,
  target.title = source.title,
  target.content = source.content,
  target.target_type = source.target_type,
  target.target_id = source.target_id,
  target.publish_at = source.publish_at,
  target.expire_at = source.expire_at,
  target.notice_status = source.notice_status
WHEN NOT MATCHED THEN INSERT (
  notice_id, club_id, publisher_user_id, notice_type, title, content,
  target_type, target_id, publish_at, expire_at, notice_status
) VALUES (
  source.notice_id, source.club_id, source.publisher_user_id, source.notice_type,
  source.title, source.content, source.target_type, source.target_id,
  source.publish_at, source.expire_at, source.notice_status
);

MERGE INTO FORUM_POSTS target
USING (
  SELECT 8101 AS post_id, 1 AS club_id, 3 AS user_id,
         CAST(NULL AS NUMBER) AS parent_post_id,
         '实践营组队与技术方向收集' AS title,
         '请大家回复自己感兴趣的方向：前端体验、后端接口、数据库设计或测试保障。' AS content,
         1 AS is_top, 'published' AS post_status
  FROM dual
) source
ON (target.post_id = source.post_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.parent_post_id = source.parent_post_id,
  target.title = source.title,
  target.content = source.content,
  target.is_top = source.is_top,
  target.post_status = source.post_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN INSERT (
  post_id, club_id, user_id, parent_post_id, title, content,
  is_top, post_status, created_at, updated_at
) VALUES (
  source.post_id, source.club_id, source.user_id, source.parent_post_id,
  source.title, source.content, source.is_top, source.post_status, SYSDATE, SYSDATE
);

COMMIT;
