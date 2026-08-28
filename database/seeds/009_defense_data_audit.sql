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

SELECT club_id, club_name, category, audit_status, club_status
FROM clubs
WHERE TRIM(LOWER(club_name)) IN ('1', '11', 'aaa', 'test', '测试')
   OR TRIM(LOWER(category)) IN ('1', 'aaa', 'test', '测试')
   OR logo_url = '1'
   OR material_url = '1';

SELECT recruit_id, title, club_id, recruit_status, start_at, end_at
FROM recruitments
WHERE TRIM(LOWER(title)) IN ('1', '2', '4', '5', 'aaa', 'test', '测试')
   OR (recruit_status = 'published' AND end_at < SYSDATE);

SELECT task_id, project_id, title, content
FROM project_tasks
WHERE TRIM(LOWER(title)) IN ('1', 'aaa', 'aaaaaa', 'test', 'test2', 'test3', '测试')
   OR content IS NULL;

SELECT application_id, title, purpose, review_comment
FROM budget_applications
WHERE TRIM(LOWER(title)) IN ('nnnn', 'aaa', 'test', '测试')
   OR TRIM(LOWER(purpose)) IN ('1', '111', 'aaa', 'test', '测试');

SELECT borrow_id, damage_desc, compensation_amount
FROM material_borrows
WHERE compensation_amount > 100000
   OR TRIM(LOWER(damage_desc)) IN ('坏了', 'test', '测试');

SELECT attachment_id, attachment_name, attachment_url
FROM award_attachments
WHERE attachment_url LIKE '/demo/%';

SELECT rule_document_id, rule_title, material_url
FROM award_rule_documents
WHERE material_url LIKE '/demo/%';
