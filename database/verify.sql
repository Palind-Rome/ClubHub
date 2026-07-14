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
  'SEQ_CLUB_DEPARTMENTS',
  'SEQ_CLUB_GROUPS',
  'SEQ_EVALUATIONS',
  'SEQ_ACTIVITIES',
  'SEQ_ACTIVITY_PARTICIPATIONS'
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
  ('CLUB_DEPARTMENTS', 'DEPARTMENT_ID'),
  ('CLUB_GROUPS', 'GROUP_ID'),
  ('EVALUATIONS', 'EVALUATION_ID'),
  ('ACTIVITIES', 'ACTIVITY_ID'),
  ('ACTIVITY_PARTICIPATIONS', 'PARTICIPATION_ID')
)
ORDER BY table_name, column_name;

-- ClubHub 核心表应为 27 张；使用固定集合计数，避免测试 schema 中的临时表干扰结果。
SELECT COUNT(*) AS clubhub_core_table_count
FROM user_tables
WHERE table_name IN (
  'USERS', 'ROLES', 'USER_ROLES',
  'CLUBS', 'CLUB_DEPARTMENTS', 'CLUB_GROUPS', 'CLUB_MEMBERS', 'RECRUITMENTS', 'RECRUITMENT_APPLICATIONS',
  'ACTIVITIES', 'ACTIVITY_PARTICIPATIONS', 'VENUES', 'VENUE_RESERVATIONS',
  'PROJECTS', 'PROJECT_MEMBERS', 'PROJECT_TASKS', 'PROJECT_TASK_ASSIGNEES', 'PROJECT_TASK_PROGRESS_REPORTS',
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

SELECT index_name, uniqueness
FROM user_indexes
WHERE index_name = 'UQ_PM_ACTIVE_LEADER';

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

SELECT project_id, COUNT(*) AS active_leader_count
FROM project_members
WHERE member_role = 'leader' AND member_status = 'active'
GROUP BY project_id
HAVING COUNT(*) > 1;

SELECT task_id, user_id, COUNT(*) AS duplicate_count
FROM project_task_assignees
GROUP BY task_id, user_id
HAVING COUNT(*) > 1;

SELECT task_progress_report_id, progress, task_status
FROM project_task_progress_reports
WHERE progress NOT BETWEEN 0 AND 100
   OR task_status NOT IN ('pending', 'in_progress', 'completed', 'delayed');

SELECT column_id, column_name, data_type, nullable, data_default
FROM user_tab_columns
WHERE table_name IN ('CLUB_DEPARTMENTS', 'CLUB_GROUPS')
ORDER BY table_name, column_id;

SELECT column_id, column_name, data_type, nullable
FROM user_tab_columns
WHERE table_name = 'CLUB_MEMBERS'
  AND column_name IN ('DEPARTMENT_ID', 'GROUP_ID', 'DEPARTMENT_NAME', 'GROUP_NAME')
ORDER BY column_id;

SELECT constraint_name, table_name, constraint_type, status, deferrable, deferred
FROM user_constraints
WHERE constraint_name IN (
  'FK_CLUB_DEPARTMENTS_CLUB',
  'FK_CLUB_GROUPS_DEPARTMENT',
  'CK_CLUB_MEMBERS_GROUP_DEPT',
  'FK_CLUB_MEMBERS_DEPARTMENT',
  'FK_CLUB_MEMBERS_GROUP',
  'UQ_CLUB_DEPARTMENTS_SCOPE',
  'UQ_CLUB_DEPARTMENTS_NAME',
  'UQ_CLUB_GROUPS_SCOPE',
  'UQ_CLUB_GROUPS_DEPT_SCOPE',
  'UQ_CLUB_GROUPS_NAME'
)
ORDER BY table_name, constraint_type, constraint_name;

SELECT index_name, table_name, uniqueness
FROM user_indexes
WHERE index_name IN (
  'IX_CLUB_DEPARTMENTS_ORDER',
  'IX_CLUB_GROUPS_ORDER',
  'IX_CLUB_MEMBERS_ORG'
)
ORDER BY index_name;

-- 以下查询均应返回 0 行。
SELECT department_id, club_id, department_name, COUNT(*) AS duplicate_count
FROM club_departments
GROUP BY department_id, club_id, department_name
HAVING COUNT(*) > 1;

SELECT club_id, department_name, COUNT(*) AS duplicate_count
FROM club_departments
GROUP BY club_id, department_name
HAVING COUNT(*) > 1;

SELECT g.group_id, g.club_id, g.department_id
FROM club_groups g
WHERE NOT EXISTS (
  SELECT 1
  FROM club_departments d
  WHERE d.club_id = g.club_id
    AND d.department_id = g.department_id
);

SELECT club_id, department_id, group_name, COUNT(*) AS duplicate_count
FROM club_groups
GROUP BY club_id, department_id, group_name
HAVING COUNT(*) > 1;

SELECT member_id, club_id, department_name
FROM club_members
WHERE department_name IS NOT NULL
  AND TRIM(department_name) IS NOT NULL
  AND department_id IS NULL;

SELECT member_id, club_id, department_id, group_name
FROM club_members
WHERE group_name IS NOT NULL
  AND TRIM(group_name) IS NOT NULL
  AND group_id IS NULL;

SELECT member_id, club_id, department_id, group_id
FROM club_members
WHERE group_id IS NOT NULL
  AND department_id IS NULL;

SELECT cm.member_id, cm.club_id, cm.department_id
FROM club_members cm
WHERE cm.department_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM club_departments d
    WHERE d.club_id = cm.club_id
      AND d.department_id = cm.department_id
  );

SELECT cm.member_id, cm.club_id, cm.department_id, cm.group_id
FROM club_members cm
WHERE cm.group_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM club_groups g
    WHERE g.club_id = cm.club_id
      AND g.department_id = cm.department_id
      AND g.group_id = cm.group_id
  );
