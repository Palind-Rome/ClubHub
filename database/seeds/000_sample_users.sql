-- 用户与全局角色样例数据：用于本地或明确测试库演示角色化社团注册流程。
-- 所有样例账号密码均为 123456；重复执行会重置样例账号密码。

MERGE INTO USERS target
USING (
  SELECT 1 AS user_id, 'student_chen' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '陈同学' AS real_name, '2350001' AS student_no,
         '计算机科学与技术学院' AS college, '软件工程' AS major,
         '2023' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 2 AS user_id, 'admin_li' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '李老师' AS real_name, '06002' AS student_no,
         '学生社团管理中心' AS college, '社团管理' AS major,
         '教师' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 3 AS user_id, 'president_wang' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '王会长' AS real_name, '2250003' AS student_no,
         '电子与信息工程学院' AS college, '人工智能' AS major,
         '2022' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 4 AS user_id, 'member_liu' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '刘同学' AS real_name, '2450004' AS student_no,
         '设计创意学院' AS college, '工业设计' AS major,
         '2024' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 5 AS user_id, 'advisor_zhang' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '张老师' AS real_name, '06005' AS student_no,
         '电子与信息工程学院' AS college, '指导教师' AS major,
         '教师' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 6 AS user_id, 'officer_sun' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '孙干部' AS real_name, '2350006' AS student_no,
         '计算机科学与技术学院' AS college, '软件工程' AS major,
         '2023' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO USERS target
USING (
  SELECT 7 AS user_id, 'zhang_guoxiong' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '张国雄' AS real_name, '2350007' AS student_no,
         '经济与管理学院' AS college, '信息管理与信息系统' AS major,
         '2023' AS grade, 'active' AS account_status
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 1 AS role_id, 'STUDENT' AS role_code, '普通学生' AS role_name,
         'system' AS role_scope, '注册后默认角色，可维护个人信息、浏览公开内容、申请社团和参与报名。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 2 AS role_id, 'TEACHER' AS role_code, '教师' AS role_name,
         'system' AS role_scope, '教师基础身份，可维护个人信息并浏览公开社团、招募、活动和公告。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 3 AS role_id, 'CLUB_MEMBER' AS role_code, '社团成员' AS role_name,
         'club' AS role_scope, '指定社团内角色，可查看社团内部信息、资源、通知和参与讨论、签到。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 4 AS role_id, 'CLUB_OFFICER' AS role_code, '社团干部' AS role_name,
         'club' AS role_scope, '指定社团内角色，可管理招募、活动、通知、资源、项目任务，并处理本社团物资借还记录。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 5 AS role_id, 'CLUB_LEADER' AS role_code, '社团负责人' AS role_name,
         'club' AS role_scope, '指定社团内最高业务角色，可维护社团信息、成员、社团内部角色、运营统计和本社团物资库存。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 6 AS role_id, 'ADVISOR' AS role_code, '指导老师' AS role_name,
         'club' AS role_scope, '指定社团指导角色，可查看社团运营、处理本社团物资借还记录，并审核活动、项目、经费和评价。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 7 AS role_id, 'CLUB_ADMIN' AS role_code, '社团管理员' AS role_name,
         'system' AS role_scope, '校级社团管理角色，可审核社团注册申请、管理社团状态并维护全校社团物资库存，不参与社团内部档案、成员任期和干部换届维护。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO ROLES target
USING (
  SELECT 8 AS role_id, 'SYSTEM_ADMIN' AS role_code, '系统管理员' AS role_name,
         'system' AS role_scope, '系统最高角色，可管理账号状态、全局角色、系统日志和系统级数据。' AS permission_desc
  FROM dual
) source
ON (target.role_id = source.role_id)
WHEN MATCHED THEN UPDATE SET
  target.role_code = source.role_code,
  target.role_name = source.role_name,
  target.role_scope = source.role_scope,
  target.permission_desc = source.permission_desc
WHEN NOT MATCHED THEN
  INSERT (role_id, role_code, role_name, role_scope, permission_desc, created_at)
  VALUES (source.role_id, source.role_code, source.role_name, source.role_scope, source.permission_desc, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 1 AS user_role_id, 1 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 2 AS user_role_id, 2 AS user_id, 2 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 3 AS user_role_id, 2 AS user_id, 7 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 4 AS user_role_id, 3 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 5 AS user_role_id, 4 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 6 AS user_role_id, 5 AS user_id, 2 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 7 AS user_role_id, 6 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 8 AS user_role_id, 7 AS user_id, 1 AS role_id, CAST(NULL AS NUMBER) AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

COMMIT;
