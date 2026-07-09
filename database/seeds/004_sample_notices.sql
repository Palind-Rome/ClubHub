-- 公告通知与已读记录演示数据
-- 依赖 000_sample_users.sql 与 001_sample_clubs.sql 中的示例用户、社团和成员任期。

MERGE INTO NOTICES target
USING (
  SELECT 1 AS notice_id,
         NULL AS club_id,
         1 AS publisher_user_id,
         'announcement' AS notice_type,
         '校级社团系统试运行通知' AS title,
         'ClubHub 公告通知模块进入试运行，请各社团负责人及时查看并反馈问题。' AS content,
         'school' AS target_type,
         NULL AS target_id,
         SYSDATE - 2 AS publish_at,
         SYSDATE + 30 AS expire_at,
         'published' AS notice_status
  FROM dual
) src
ON (target.notice_id = src.notice_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = src.club_id,
  target.publisher_user_id = src.publisher_user_id,
  target.notice_type = src.notice_type,
  target.title = src.title,
  target.content = src.content,
  target.target_type = src.target_type,
  target.target_id = src.target_id,
  target.publish_at = src.publish_at,
  target.expire_at = src.expire_at,
  target.notice_status = src.notice_status
WHEN NOT MATCHED THEN INSERT (
  notice_id, club_id, publisher_user_id, notice_type, title, content,
  target_type, target_id, publish_at, expire_at, notice_status
) VALUES (
  src.notice_id, src.club_id, src.publisher_user_id, src.notice_type, src.title, src.content,
  src.target_type, src.target_id, src.publish_at, src.expire_at, src.notice_status
);

MERGE INTO NOTICES target
USING (
  SELECT 2 AS notice_id,
         1 AS club_id,
         2 AS publisher_user_id,
         'event' AS notice_type,
         '计算机协会本周例会' AS title,
         '本周例会将讨论 Hackathon 筹备进度，请当前成员准时参加。' AS content,
         'club' AS target_type,
         1 AS target_id,
         SYSDATE - 1 AS publish_at,
         SYSDATE + 7 AS expire_at,
         'published' AS notice_status
  FROM dual
) src
ON (target.notice_id = src.notice_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = src.club_id,
  target.publisher_user_id = src.publisher_user_id,
  target.notice_type = src.notice_type,
  target.title = src.title,
  target.content = src.content,
  target.target_type = src.target_type,
  target.target_id = src.target_id,
  target.publish_at = src.publish_at,
  target.expire_at = src.expire_at,
  target.notice_status = src.notice_status
WHEN NOT MATCHED THEN INSERT (
  notice_id, club_id, publisher_user_id, notice_type, title, content,
  target_type, target_id, publish_at, expire_at, notice_status
) VALUES (
  src.notice_id, src.club_id, src.publisher_user_id, src.notice_type, src.title, src.content,
  src.target_type, src.target_id, src.publish_at, src.expire_at, src.notice_status
);

MERGE INTO NOTICE_READS target
USING (
  SELECT 1 AS read_id,
         1 AS notice_id,
         2 AS user_id,
         SYSDATE - 1 AS read_at
  FROM dual
) src
ON (target.read_id = src.read_id)
WHEN MATCHED THEN UPDATE SET
  target.notice_id = src.notice_id,
  target.user_id = src.user_id,
  target.read_at = src.read_at
WHEN NOT MATCHED THEN INSERT (read_id, notice_id, user_id, read_at)
VALUES (src.read_id, src.notice_id, src.user_id, src.read_at);

COMMIT;
