SELECT USER AS current_user FROM dual;

SELECT COUNT(*) AS table_count
FROM user_tables;

SELECT table_name
FROM user_tables
ORDER BY table_name;

SELECT sequence_name, last_number
FROM user_sequences
WHERE sequence_name IN (
  'SEQ_USERS',
  'SEQ_USER_ROLES',
  'SEQ_CLUBS',
  'SEQ_CLUB_MEMBERS',
  'SEQ_EVALUATIONS'
)
ORDER BY sequence_name;

SELECT index_name, table_name, uniqueness
FROM user_indexes
WHERE index_name IN (
  'UQ_USERS_USERNAME',
  'UQ_USERS_STUDENT_NO',
  'UQ_USER_ROLES_SCOPE'
)
ORDER BY index_name;

SELECT table_name, column_name, data_default
FROM user_tab_columns
WHERE (table_name, column_name) IN (
  ('USERS', 'USER_ID'),
  ('USER_ROLES', 'USER_ROLE_ID'),
  ('CLUBS', 'CLUB_ID'),
  ('CLUB_MEMBERS', 'MEMBER_ID'),
  ('EVALUATIONS', 'EVALUATION_ID')
)
ORDER BY table_name, column_name;

-- ClubHub 核心表应为 23 张；使用固定集合计数，避免测试 schema 中的临时表干扰结果。
SELECT COUNT(*) AS clubhub_core_table_count
FROM user_tables
WHERE table_name IN (
  'USERS', 'ROLES', 'USER_ROLES',
  'CLUBS', 'CLUB_MEMBERS', 'RECRUITMENTS', 'RECRUITMENT_APPLICATIONS',
  'ACTIVITIES', 'ACTIVITY_PARTICIPATIONS', 'VENUES', 'VENUE_RESERVATIONS',
  'PROJECTS', 'PROJECT_MEMBERS', 'PROJECT_TASKS',
  'LEARNING_ITEMS', 'LEARNING_RECORDS',
  'MATERIALS', 'MATERIAL_BORROWS', 'EVALUATIONS',
  'NOTICES', 'NOTICE_READS', 'FORUM_POSTS', 'OPERATION_LOGS'
);

SELECT column_id, column_name, data_type, data_length, nullable, data_default
FROM user_tab_columns
WHERE table_name = 'PROJECT_MEMBERS'
ORDER BY column_id;

SELECT constraint_name, constraint_type, status, deferrable, deferred
FROM user_constraints
WHERE table_name = 'PROJECT_MEMBERS'
ORDER BY constraint_type, constraint_name;

-- 以下查询均应返回 0 行。
SELECT project_id, user_id, COUNT(*) AS duplicate_count
FROM project_members
GROUP BY project_id, user_id
HAVING COUNT(*) > 1;

SELECT project_member_id, member_role, member_status
FROM project_members
WHERE member_role NOT IN ('leader', 'member', 'mentor')
   OR member_status NOT IN ('active', 'removed', 'quit');

SELECT p.project_id, p.leader_user_id
FROM projects p
WHERE p.leader_user_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM project_members pm
    WHERE pm.project_id = p.project_id
      AND pm.user_id = p.leader_user_id
      AND pm.member_role = 'leader'
      AND pm.member_status = 'active'
  );
