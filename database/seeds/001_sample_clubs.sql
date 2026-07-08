-- 社团样例数据：依赖 000_sample_users.sql

MERGE INTO CLUBS target
USING (
  SELECT 1 AS club_id, '计算机协会' AS club_name, '学术科技' AS category,
         '编程竞赛、技术分享、黑客松组织' AS description,
         3 AS president_user_id, '张老师' AS advisor_name,
         '021-00000001' AS contact_phone, 'approved' AS audit_status,
         2 AS reviewer_user_id, '材料齐全，准予成立。' AS review_comment,
         'active' AS club_status, DATE '2024-09-01' AS founded_at
  FROM dual
) source
ON (target.club_id = source.club_id)
WHEN MATCHED THEN UPDATE SET
  target.club_name = source.club_name,
  target.category = source.category,
  target.description = source.description,
  target.president_user_id = source.president_user_id,
  target.advisor_name = source.advisor_name,
  target.contact_phone = source.contact_phone,
  target.audit_status = source.audit_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment,
  target.club_status = source.club_status,
  target.founded_at = source.founded_at,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (club_id, club_name, category, description, president_user_id, advisor_name, contact_phone, audit_status, reviewer_user_id, review_comment, club_status, founded_at, created_at, updated_at)
  VALUES (source.club_id, source.club_name, source.category, source.description, source.president_user_id, source.advisor_name, source.contact_phone, source.audit_status, source.reviewer_user_id, source.review_comment, source.club_status, source.founded_at, SYSDATE, SYSDATE);

MERGE INTO CLUBS target
USING (
  SELECT 2 AS club_id, '摄影社' AS club_name, '文化艺术' AS category,
         '校园摄影采风、人像摄影教学、作品展览' AS description,
         CAST(NULL AS NUMBER) AS president_user_id, '林老师' AS advisor_name,
         '021-00000002' AS contact_phone, 'approved' AS audit_status,
         2 AS reviewer_user_id, '历史社团数据。' AS review_comment,
         'active' AS club_status, DATE '2024-09-15' AS founded_at
  FROM dual
) source
ON (target.club_id = source.club_id)
WHEN MATCHED THEN UPDATE SET
  target.club_name = source.club_name,
  target.category = source.category,
  target.description = source.description,
  target.president_user_id = source.president_user_id,
  target.advisor_name = source.advisor_name,
  target.contact_phone = source.contact_phone,
  target.audit_status = source.audit_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment,
  target.club_status = source.club_status,
  target.founded_at = source.founded_at,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (club_id, club_name, category, description, president_user_id, advisor_name, contact_phone, audit_status, reviewer_user_id, review_comment, club_status, founded_at, created_at, updated_at)
  VALUES (source.club_id, source.club_name, source.category, source.description, source.president_user_id, source.advisor_name, source.contact_phone, source.audit_status, source.reviewer_user_id, source.review_comment, source.club_status, source.founded_at, SYSDATE, SYSDATE);

MERGE INTO CLUBS target
USING (
  SELECT 3 AS club_id, '羽毛球协会' AS club_name, '体育竞技' AS category,
         '每周训练、校内联赛、校际交流赛' AS description,
         7 AS president_user_id, '周老师' AS advisor_name,
         '021-00000003' AS contact_phone, 'approved' AS audit_status,
         2 AS reviewer_user_id, '历史社团数据。' AS review_comment,
         'active' AS club_status, DATE '2024-03-10' AS founded_at
  FROM dual
) source
ON (target.club_id = source.club_id)
WHEN MATCHED THEN UPDATE SET
  target.club_name = source.club_name,
  target.category = source.category,
  target.description = source.description,
  target.president_user_id = source.president_user_id,
  target.advisor_name = source.advisor_name,
  target.contact_phone = source.contact_phone,
  target.audit_status = source.audit_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment,
  target.club_status = source.club_status,
  target.founded_at = source.founded_at,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (club_id, club_name, category, description, president_user_id, advisor_name, contact_phone, audit_status, reviewer_user_id, review_comment, club_status, founded_at, created_at, updated_at)
  VALUES (source.club_id, source.club_name, source.category, source.description, source.president_user_id, source.advisor_name, source.contact_phone, source.audit_status, source.reviewer_user_id, source.review_comment, source.club_status, source.founded_at, SYSDATE, SYSDATE);

MERGE INTO CLUBS target
USING (
  SELECT 4 AS club_id, '辩论队' AS club_name, '学术科技' AS category,
         '辩论技巧训练、校内辩论赛、校际交流' AS description,
         CAST(NULL AS NUMBER) AS president_user_id, '赵老师' AS advisor_name,
         '021-00000004' AS contact_phone, 'approved' AS audit_status,
         2 AS reviewer_user_id, '历史社团数据。' AS review_comment,
         'active' AS club_status, DATE '2023-11-01' AS founded_at
  FROM dual
) source
ON (target.club_id = source.club_id)
WHEN MATCHED THEN UPDATE SET
  target.club_name = source.club_name,
  target.category = source.category,
  target.description = source.description,
  target.president_user_id = source.president_user_id,
  target.advisor_name = source.advisor_name,
  target.contact_phone = source.contact_phone,
  target.audit_status = source.audit_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment,
  target.club_status = source.club_status,
  target.founded_at = source.founded_at,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (club_id, club_name, category, description, president_user_id, advisor_name, contact_phone, audit_status, reviewer_user_id, review_comment, club_status, founded_at, created_at, updated_at)
  VALUES (source.club_id, source.club_name, source.category, source.description, source.president_user_id, source.advisor_name, source.contact_phone, source.audit_status, source.reviewer_user_id, source.review_comment, source.club_status, source.founded_at, SYSDATE, SYSDATE);

MERGE INTO CLUBS target
USING (
  SELECT 5 AS club_id, '志愿者协会' AS club_name, '公益实践' AS category,
         '社区服务、支教活动、公益项目' AS description,
         CAST(NULL AS NUMBER) AS president_user_id, '钱老师' AS advisor_name,
         '021-00000005' AS contact_phone, 'approved' AS audit_status,
         2 AS reviewer_user_id, '历史社团数据。' AS review_comment,
         'active' AS club_status, DATE '2024-01-15' AS founded_at
  FROM dual
) source
ON (target.club_id = source.club_id)
WHEN MATCHED THEN UPDATE SET
  target.club_name = source.club_name,
  target.category = source.category,
  target.description = source.description,
  target.president_user_id = source.president_user_id,
  target.advisor_name = source.advisor_name,
  target.contact_phone = source.contact_phone,
  target.audit_status = source.audit_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment,
  target.club_status = source.club_status,
  target.founded_at = source.founded_at,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (club_id, club_name, category, description, president_user_id, advisor_name, contact_phone, audit_status, reviewer_user_id, review_comment, club_status, founded_at, created_at, updated_at)
  VALUES (source.club_id, source.club_name, source.category, source.description, source.president_user_id, source.advisor_name, source.contact_phone, source.audit_status, source.reviewer_user_id, source.review_comment, source.club_status, source.founded_at, SYSDATE, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT 101 AS user_role_id, 3 AS user_id, 5 AS role_id, 1 AS club_id FROM dual
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
  SELECT 102 AS user_role_id, 4 AS user_id, 3 AS role_id, 1 AS club_id FROM dual
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
  SELECT 103 AS user_role_id, 5 AS user_id, 6 AS role_id, 1 AS club_id FROM dual
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
  SELECT 104 AS user_role_id, 6 AS user_id, 4 AS role_id, 1 AS club_id FROM dual
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
  SELECT 105 AS user_role_id, 7 AS user_id, 3 AS role_id, 1 AS club_id FROM dual
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
  SELECT 106 AS user_role_id, 7 AS user_id, 4 AS role_id, 2 AS club_id FROM dual
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
  SELECT 107 AS user_role_id, 7 AS user_id, 5 AS role_id, 3 AS club_id FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 1 AS member_id, 1 AS club_id, 3 AS user_id, '主席团' AS department_name,
         '负责人组' AS group_name, '负责人' AS position_name,
         '当前演示任期' AS term_name, TRUNC(SYSDATE) - 30 AS term_start,
         ADD_MONTHS(TRUNC(SYSDATE), 12) AS term_end, 'active' AS member_status,
         TRUNC(SYSDATE) - 30 AS join_at, 95 AS contribution_score
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 2 AS member_id, 1 AS club_id, 4 AS user_id, '技术部' AS department_name,
         '开发组' AS group_name, '社员' AS position_name,
         '当前演示任期' AS term_name, TRUNC(SYSDATE) - 20 AS term_start,
         ADD_MONTHS(TRUNC(SYSDATE), 12) AS term_end, 'active' AS member_status,
         TRUNC(SYSDATE) - 20 AS join_at, 72 AS contribution_score
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 3 AS member_id, 1 AS club_id, 6 AS user_id, '活动部' AS department_name,
         '赛事组' AS group_name, '部长' AS position_name,
         '当前演示任期' AS term_name, TRUNC(SYSDATE) - 25 AS term_start,
         ADD_MONTHS(TRUNC(SYSDATE), 12) AS term_end, 'active' AS member_status,
         TRUNC(SYSDATE) - 25 AS join_at, 88 AS contribution_score
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 4 AS member_id, 1 AS club_id, 7 AS user_id, '算法部' AS department_name,
         '训练组' AS group_name, '社员' AS position_name,
         '当前演示任期' AS term_name, TRUNC(SYSDATE) - 18 AS term_start,
         ADD_MONTHS(TRUNC(SYSDATE), 12) AS term_end, 'active' AS member_status,
         TRUNC(SYSDATE) - 18 AS join_at, 66 AS contribution_score
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 5 AS member_id, 2 AS club_id, 7 AS user_id, '活动部' AS department_name,
         '影展组' AS group_name, '部长' AS position_name,
         '当前演示任期' AS term_name, TRUNC(SYSDATE) - 16 AS term_start,
         ADD_MONTHS(TRUNC(SYSDATE), 12) AS term_end, 'active' AS member_status,
         TRUNC(SYSDATE) - 16 AS join_at, 82 AS contribution_score
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 6 AS member_id, 3 AS club_id, 7 AS user_id, '主席团' AS department_name,
         '负责人组' AS group_name, '负责人' AS position_name,
         '当前演示任期' AS term_name, TRUNC(SYSDATE) - 14 AS term_start,
         ADD_MONTHS(TRUNC(SYSDATE), 12) AS term_end, 'active' AS member_status,
         TRUNC(SYSDATE) - 14 AS join_at, 91 AS contribution_score
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

COMMIT;
