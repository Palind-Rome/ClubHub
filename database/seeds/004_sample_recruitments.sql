-- 招募与报名样例数据：依赖 000_sample_users.sql 和 001_sample_clubs.sql。
-- 可用于演示社团干部发布招募、学生报名、干部筛选录取流程。

MERGE INTO RECRUITMENTS target
USING (
  SELECT 1 AS recruit_id, 1 AS club_id,
         '计算机协会 2026 春季技术部招新' AS title,
         '面向对算法竞赛、Web 开发和开源项目感兴趣的同学开放报名。' AS description,
         SYSDATE - 3 AS start_at,
         SYSDATE + 14 AS end_at,
         5 AS quota,
         '请说明技术方向、项目经历或希望参与的活动。' AS requirements,
         'published' AS recruit_status,
         3 AS creator_user_id
  FROM dual
) source
ON (target.recruit_id = source.recruit_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.title = source.title,
  target.description = source.description,
  target.start_at = source.start_at,
  target.end_at = source.end_at,
  target.quota = source.quota,
  target.requirements = source.requirements,
  target.recruit_status = source.recruit_status,
  target.creator_user_id = source.creator_user_id
WHEN NOT MATCHED THEN
  INSERT (recruit_id, club_id, title, description, start_at, end_at, quota, requirements, recruit_status, creator_user_id, created_at)
  VALUES (source.recruit_id, source.club_id, source.title, source.description, source.start_at, source.end_at, source.quota, source.requirements, source.recruit_status, source.creator_user_id, SYSDATE);

MERGE INTO RECRUITMENTS target
USING (
  SELECT 2 AS recruit_id, 2 AS club_id,
         '摄影社 校园影像记录小组招新' AS title,
         '招募负责校园活动跟拍、后期修图和主题影展策划的成员。' AS description,
         SYSDATE - 7 AS start_at,
         SYSDATE + 7 AS end_at,
         3 AS quota,
         '请提交摄影兴趣方向、设备情况或过往作品说明。' AS requirements,
         'published' AS recruit_status,
         7 AS creator_user_id
  FROM dual
) source
ON (target.recruit_id = source.recruit_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.title = source.title,
  target.description = source.description,
  target.start_at = source.start_at,
  target.end_at = source.end_at,
  target.quota = source.quota,
  target.requirements = source.requirements,
  target.recruit_status = source.recruit_status,
  target.creator_user_id = source.creator_user_id
WHEN NOT MATCHED THEN
  INSERT (recruit_id, club_id, title, description, start_at, end_at, quota, requirements, recruit_status, creator_user_id, created_at)
  VALUES (source.recruit_id, source.club_id, source.title, source.description, source.start_at, source.end_at, source.quota, source.requirements, source.recruit_status, source.creator_user_id, SYSDATE);

MERGE INTO RECRUITMENTS target
USING (
  SELECT 3 AS recruit_id, 3 AS club_id,
         '羽毛球协会 校内联赛志愿组补招' AS title,
         '补充校内联赛现场组织、计分和物资协助成员。' AS description,
         SYSDATE - 21 AS start_at,
         SYSDATE - 1 AS end_at,
         2 AS quota,
         '需要能参加周末联赛值班，有赛事组织经验优先。' AS requirements,
         'closed' AS recruit_status,
         7 AS creator_user_id
  FROM dual
) source
ON (target.recruit_id = source.recruit_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.title = source.title,
  target.description = source.description,
  target.start_at = source.start_at,
  target.end_at = source.end_at,
  target.quota = source.quota,
  target.requirements = source.requirements,
  target.recruit_status = source.recruit_status,
  target.creator_user_id = source.creator_user_id
WHEN NOT MATCHED THEN
  INSERT (recruit_id, club_id, title, description, start_at, end_at, quota, requirements, recruit_status, creator_user_id, created_at)
  VALUES (source.recruit_id, source.club_id, source.title, source.description, source.start_at, source.end_at, source.quota, source.requirements, source.recruit_status, source.creator_user_id, SYSDATE);

MERGE INTO RECRUITMENT_APPLICATIONS target
USING (
  SELECT 1 AS application_id, 2 AS recruit_id, 1 AS user_id,
         '希望加入摄影社校园影像记录小组，参与活动跟拍和后期修图。' AS application_reason,
         CAST(NULL AS NUMBER) AS interview_score,
         'pending' AS application_status,
         CAST(NULL AS NUMBER) AS reviewer_user_id,
         SYSDATE - 1 AS submitted_at,
         CAST(NULL AS DATE) AS reviewed_at
  FROM dual
) source
ON (target.application_id = source.application_id)
WHEN MATCHED THEN UPDATE SET
  target.recruit_id = source.recruit_id,
  target.user_id = source.user_id,
  target.application_reason = source.application_reason,
  target.interview_score = source.interview_score,
  target.application_status = source.application_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.submitted_at = source.submitted_at,
  target.reviewed_at = source.reviewed_at
WHEN NOT MATCHED THEN
  INSERT (application_id, recruit_id, user_id, application_reason, interview_score, application_status, reviewer_user_id, submitted_at, reviewed_at)
  VALUES (source.application_id, source.recruit_id, source.user_id, source.application_reason, source.interview_score, source.application_status, source.reviewer_user_id, source.submitted_at, source.reviewed_at);

MERGE INTO RECRUITMENT_APPLICATIONS target
USING (
  SELECT 2 AS application_id, 3 AS recruit_id, 6 AS user_id,
         '可参与周末赛事现场组织，熟悉活动签到和物资流转。' AS application_reason,
         78 AS interview_score,
         'rejected' AS application_status,
         7 AS reviewer_user_id,
         SYSDATE - 10 AS submitted_at,
         SYSDATE - 2 AS reviewed_at
  FROM dual
) source
ON (target.application_id = source.application_id)
WHEN MATCHED THEN UPDATE SET
  target.recruit_id = source.recruit_id,
  target.user_id = source.user_id,
  target.application_reason = source.application_reason,
  target.interview_score = source.interview_score,
  target.application_status = source.application_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.submitted_at = source.submitted_at,
  target.reviewed_at = source.reviewed_at
WHEN NOT MATCHED THEN
  INSERT (application_id, recruit_id, user_id, application_reason, interview_score, application_status, reviewer_user_id, submitted_at, reviewed_at)
  VALUES (source.application_id, source.recruit_id, source.user_id, source.application_reason, source.interview_score, source.application_status, source.reviewer_user_id, source.submitted_at, source.reviewed_at);

COMMIT;
