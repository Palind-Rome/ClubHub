-- 用户与全局角色样例数据：用于本地或明确测试库演示角色化社团注册流程

MERGE INTO USERS target
USING (
  SELECT 1 AS user_id, 'student_chen' AS username, '陈同学' AS real_name,
         '2350001' AS student_no, '计算机科学与技术学院' AS college,
         '软件工程' AS major, '2023' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN NOT MATCHED THEN
  INSERT (user_id, username, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 2 AS user_id, 'admin_li' AS username, '李老师' AS real_name,
         'T2026002' AS student_no, '学生社团管理中心' AS college,
         '社团管理' AS major, '教师' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN NOT MATCHED THEN
  INSERT (user_id, username, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 3 AS user_id, 'president_wang' AS username, '王会长' AS real_name,
         '2250003' AS student_no, '电子与信息工程学院' AS college,
         '人工智能' AS major, '2022' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN NOT MATCHED THEN
  INSERT (user_id, username, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 4 AS user_id, 'student_liu' AS username, '刘同学' AS real_name,
         '2450004' AS student_no, '设计创意学院' AS college,
         '工业设计' AS major, '2024' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN NOT MATCHED THEN
  INSERT (user_id, username, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 1 AS role_id, 'student' AS role_code, '学生' AS role_name,
         'global' AS role_scope, '提交社团注册申请，查看本人申请状态。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 2 AS role_id, 'platform_admin' AS role_code, '平台管理员' AS role_name,
         'global' AS role_scope, '审核社团注册申请，查看全部申请状态。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 3 AS role_id, 'club_president' AS role_code, '社团负责人' AS role_name,
         'club' AS role_scope, '维护本社团基础信息、成员与干部任期。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 4 AS role_id, 'club_member' AS role_code, '社团成员' AS role_name,
         'club' AS role_scope, '查看本人社团成员身份和任期记录。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 1 AS user_role_id, 1 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 2 AS user_role_id, 2 AS user_id, 2 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 3 AS user_role_id, 3 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 4 AS user_role_id, 4 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

COMMIT;
