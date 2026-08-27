-- 答辩前只读数据巡检。每个查询都应返回 0 行；本脚本不会修改任何数据。

SELECT activity_id, title, location
FROM activities
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试', '驳回')
   OR TRIM(LOWER(location)) IN ('1', 'aaa', 'test', '测试')
   OR (activity_status IN ('published', 'ongoing') AND end_at < SYSDATE);

SELECT project_id, project_name, club_id, leader_user_id
FROM projects
WHERE TRIM(LOWER(project_name)) IN ('1', 'aaa', 'zzzz', 'test', '测试')
   OR club_id IS NULL
   OR leader_user_id IS NULL
   OR NOT EXISTS (SELECT 1 FROM clubs WHERE clubs.club_id = projects.club_id)
   OR NOT EXISTS (SELECT 1 FROM users WHERE users.user_id = projects.leader_user_id);

SELECT notice_id, title, content
FROM notices
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试')
   OR content IS NULL;

SELECT item_id, title, description
FROM learning_items
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试')
   OR description IS NULL;

SELECT post_id, title, content
FROM forum_posts
WHERE parent_post_id IS NULL
  AND (TRIM(LOWER(title)) IN ('1', 'aaa', 'test', '测试') OR title IS NULL);
