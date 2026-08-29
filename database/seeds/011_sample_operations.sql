-- 答辩前的运营样例补充：完善核心账号档案，并补齐近期活动、场地、经费和物资借还数据。
-- 只更新下方明确列出的展示账号和固定样例 ID；不删除未知记录，可重复执行。

SET DEFINE OFF;
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  v_required_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO v_required_count
  FROM (
    SELECT 1 FROM USERS WHERE student_no = '2450004'
    UNION ALL SELECT 1 FROM USERS WHERE student_no = '05001'
    UNION ALL SELECT 1 FROM USERS WHERE student_no = '2450003'
    UNION ALL SELECT 1 FROM CLUBS WHERE club_id = 1
    UNION ALL SELECT 1 FROM BUDGET_ACCOUNTS WHERE account_id = 1000001 AND club_id = 1
  );
  IF v_required_count <> 5 THEN
    RAISE_APPLICATION_ERROR(-20191, 'Operations sample prerequisites are incomplete.');
  END IF;
END;
/

-- 展示账号统一补齐学院、专业，避免工作台出现“未填写”。
PROMPT [011] update user profiles
UPDATE USERS SET college = '计算机科学与技术学院', major = '软件工程', updated_at = SYSDATE
WHERE student_no IN ('2450001', '2450002', '2450003', '2450004', '2454288', '3544036');

UPDATE USERS SET college = '计算机科学与技术学院', major = '指导教师', updated_at = SYSDATE
WHERE student_no IN ('06026', '05001');

UPDATE USERS SET college = '学生工作部', major = '学生社团管理', updated_at = SYSDATE
WHERE student_no IN ('05002', '05003');

UPDATE USERS SET college = '体育教学部', major = '体育教育', updated_at = SYSDATE
WHERE student_no = '2351510';

UPDATE USERS SET college = '电子与信息工程学院', major = '人工智能', updated_at = SYSDATE
WHERE student_no = '2553134';

UPDATE ACTIVITIES
SET review_comment = '活动已按计划完成并归档。'
WHERE activity_id = 4 AND review_comment = '????';

-- 账号基础角色和当前成员角色与学工号、有效任期保持一致。
MERGE INTO USER_ROLES target
USING (
  SELECT u.user_id, r.role_id
  FROM USERS u
  JOIN ROLES r ON r.role_code = 'STUDENT'
  WHERE REGEXP_LIKE(u.student_no, '^[0-9]{7}$')
) source
ON (target.user_id = source.user_id AND target.role_id = source.role_id AND target.club_id IS NULL)
WHEN NOT MATCHED THEN INSERT
  (user_role_id, user_id, role_id, club_id, assigned_at)
VALUES
  (SEQ_USER_ROLES.NEXTVAL, source.user_id, source.role_id, NULL, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT u.user_id, r.role_id
  FROM USERS u
  JOIN ROLES r ON r.role_code = 'TEACHER'
  WHERE REGEXP_LIKE(u.student_no, '^[0-9]{5}$')
) source
ON (target.user_id = source.user_id AND target.role_id = source.role_id AND target.club_id IS NULL)
WHEN NOT MATCHED THEN INSERT
  (user_role_id, user_id, role_id, club_id, assigned_at)
VALUES
  (SEQ_USER_ROLES.NEXTVAL, source.user_id, source.role_id, NULL, SYSDATE);

MERGE INTO USER_ROLES target
USING (
  SELECT DISTINCT cm.user_id, cm.club_id, r.role_id
  FROM CLUB_MEMBERS cm
  JOIN ROLES r ON r.role_code = 'CLUB_MEMBER'
  WHERE LOWER(TRIM(NVL(cm.member_status, 'active'))) IN ('active', 'normal', 'enabled', '在任', '正常')
    AND (cm.term_end IS NULL OR cm.term_end >= TRUNC(SYSDATE))
) source
ON (target.user_id = source.user_id AND target.role_id = source.role_id AND target.club_id = source.club_id)
WHEN NOT MATCHED THEN INSERT
  (user_role_id, user_id, role_id, club_id, assigned_at)
VALUES
  (SEQ_USER_ROLES.NEXTVAL, source.user_id, source.role_id, source.club_id, SYSDATE);

PROMPT [011] merge venues
MERGE INTO VENUES target
USING (
  SELECT 1510 venue_id, 9 manager_user_id, '济事楼 405 机房' venue_name,
         '济事楼' building, '405' room_no, 60 capacity, 'available' venue_status FROM dual
  UNION ALL SELECT 1511, 9, '大学生活动中心报告厅', '大学生活动中心', '一楼报告厅', 180, 'available' FROM dual
  UNION ALL SELECT 1512, 9, '体育馆羽毛球 3 号场', '体育馆', '3 号场', 24, 'available' FROM dual
  UNION ALL SELECT 1513, 9, '图书馆研讨室 B204', '图书馆', 'B204', 20, 'available' FROM dual
) source
ON (target.venue_id = source.venue_id)
WHEN MATCHED THEN UPDATE SET
  target.manager_user_id = source.manager_user_id,
  target.venue_name = source.venue_name,
  target.building = source.building,
  target.room_no = source.room_no,
  target.capacity = source.capacity,
  target.venue_status = source.venue_status
WHEN NOT MATCHED THEN INSERT
  (venue_id, manager_user_id, venue_name, building, room_no, capacity, venue_status, created_at)
VALUES
  (source.venue_id, source.manager_user_id, source.venue_name, source.building,
   source.room_no, source.capacity, source.venue_status, SYSDATE);

PROMPT [011] merge activities
MERGE INTO ACTIVITIES target
USING (
  SELECT 8101 activity_id, 1 club_id, 6 creator_user_id,
         '计算机协会秋季迎新体验营' title, 'recruitment' activity_type,
         '通过项目展示、技术小游戏和部门交流帮助新生了解社团。' description,
         '大学生活动中心报告厅' location,
         TO_DATE('2026-09-06 14:00', 'YYYY-MM-DD HH24:MI') start_at,
         TO_DATE('2026-09-06 17:00', 'YYYY-MM-DD HH24:MI') end_at,
         80 capacity,
         TO_DATE('2026-09-04 20:00', 'YYYY-MM-DD HH24:MI') registration_deadline,
         'pending_review' activity_status, NULL reviewer_user_id, NULL review_comment,
         NULL published_at FROM dual
  UNION ALL
  SELECT 8102, 1, 5, '数据库应用开发专题讲座', 'lecture',
         '围绕 Oracle 数据建模、事务控制和系统开发实践开展专题分享。',
         '济事楼 405 机房',
         TO_DATE('2026-09-10 18:30', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-09-10 20:30', 'YYYY-MM-DD HH24:MI'),
         60, TO_DATE('2026-09-08 20:00', 'YYYY-MM-DD HH24:MI'),
         'published', 7, '主题与场地安排完整，同意发布。', SYSDATE FROM dual
  UNION ALL
  SELECT 8103, 1, 6, '社团开放日互动展位', 'promotion',
         '展示社团项目成果，提供现场咨询、报名指引和互动体验。',
         '大学生活动中心一楼大厅',
         TO_DATE('2026-09-03 10:00', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-09-03 16:00', 'YYYY-MM-DD HH24:MI'),
         120, TO_DATE('2026-09-02 18:00', 'YYYY-MM-DD HH24:MI'),
         'published', 7, '活动流程和安全预案清晰，同意发布。', SYSDATE FROM dual
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
  target.registration_deadline = source.registration_deadline,
  target.activity_status = source.activity_status,
  target.reviewer_user_id = source.reviewer_user_id,
  target.review_comment = source.review_comment,
  target.published_at = source.published_at
WHEN NOT MATCHED THEN INSERT
  (activity_id, club_id, creator_user_id, title, activity_type, description, location,
   start_at, end_at, capacity, registration_deadline, activity_status,
   reviewer_user_id, review_comment, published_at, created_at)
VALUES
  (source.activity_id, source.club_id, source.creator_user_id, source.title,
   source.activity_type, source.description, source.location, source.start_at,
   source.end_at, source.capacity, source.registration_deadline, source.activity_status,
   source.reviewer_user_id, source.review_comment, source.published_at, SYSDATE);

PROMPT [011] merge budget application
MERGE INTO BUDGET_APPLICATIONS target
USING (
  SELECT 1000001 account_id, 1 club_id, 6 applicant_user_id,
         'activity_budget' application_type, '秋季迎新体验营物料预算' title,
         1680 amount, '迎新展示与互动体验物料' purpose,
         '用于制作活动展板、报名手册、指示牌并补充现场饮用水。' detail
  FROM dual
) source
ON (target.club_id = source.club_id AND target.title = source.title)
WHEN MATCHED THEN UPDATE SET
  target.account_id = source.account_id,
  target.applicant_user_id = source.applicant_user_id,
  target.application_type = source.application_type,
  target.amount = source.amount,
  target.purpose = source.purpose,
  target.detail = source.detail,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN INSERT
  (application_id, account_id, club_id, applicant_user_id, application_type, title,
   amount, purpose, detail, application_status, submitted_at, created_at, updated_at)
VALUES
  (SEQ_BUDGET_APPLICATIONS.NEXTVAL, source.account_id, source.club_id,
   source.applicant_user_id, source.application_type, source.title, source.amount,
   source.purpose, source.detail, 'pending', SYSDATE, SYSDATE, SYSDATE);

PROMPT [011] merge materials
MERGE INTO MATERIALS target
USING (
  SELECT 18101 material_id, 1 club_id, '开发演示笔记本电脑' material_name,
         '14 英寸 / 16 GB 内存' specification, 4 total_qty, 3 available_qty,
         '济事楼 405 器材柜 A' storage_location, 'active' material_status FROM dual
  UNION ALL SELECT 18102, 1, '便携式投影仪', '1080P / HDMI', 2, 2,
         '济事楼 405 器材柜 B', 'active' FROM dual
  UNION ALL SELECT 18103, 1, '无线麦克风套装', '一拖二领夹麦', 6, 4,
         '大学生活动中心储物间', 'active' FROM dual
  UNION ALL SELECT 18104, 1, '迎新活动展板', '80 cm × 180 cm', 12, 12,
         '大学生活动中心储物间', 'active' FROM dual
  UNION ALL SELECT 18105, 1, '志愿者马甲', '蓝色均码', 40, 40,
         '大学生活动中心储物间', 'active' FROM dual
) source
ON (target.material_id = source.material_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.material_name = source.material_name,
  target.specification = source.specification,
  target.total_qty = source.total_qty,
  target.available_qty = source.available_qty,
  target.storage_location = source.storage_location,
  target.material_status = source.material_status
WHEN NOT MATCHED THEN INSERT
  (material_id, club_id, material_name, specification, total_qty, available_qty,
   storage_location, material_status, created_at)
VALUES
  (source.material_id, source.club_id, source.material_name, source.specification,
   source.total_qty, source.available_qty, source.storage_location,
   source.material_status, SYSDATE);

PROMPT [011] merge material borrows
MERGE INTO MATERIAL_BORROWS target
USING (
  SELECT 18201 borrow_id, 18101 material_id, 1 club_id, 5 borrower_user_id,
         1 quantity, TO_DATE('2026-08-28 14:00', 'YYYY-MM-DD HH24:MI') borrow_at,
         TO_DATE('2026-09-05 18:00', 'YYYY-MM-DD HH24:MI') expected_return_at,
         NULL return_at, 'borrowed' borrow_status FROM dual
  UNION ALL SELECT 18202, 18102, 1, 6, 1,
         TO_DATE('2026-08-20 09:00', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-08-21 20:00', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-08-21 18:30', 'YYYY-MM-DD HH24:MI'), 'returned' FROM dual
  UNION ALL SELECT 18203, 18103, 1, 5, 2,
         TO_DATE('2026-08-29 10:00', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-09-06 18:00', 'YYYY-MM-DD HH24:MI'),
         NULL, 'borrowed' FROM dual
  UNION ALL SELECT 18204, 18104, 1, 6, 3,
         TO_DATE('2026-08-18 10:00', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-08-19 18:00', 'YYYY-MM-DD HH24:MI'),
         TO_DATE('2026-08-19 17:20', 'YYYY-MM-DD HH24:MI'), 'returned' FROM dual
) source
ON (target.borrow_id = source.borrow_id)
WHEN MATCHED THEN UPDATE SET
  target.material_id = source.material_id,
  target.club_id = source.club_id,
  target.borrower_user_id = source.borrower_user_id,
  target.quantity = source.quantity,
  target.borrow_at = source.borrow_at,
  target.expected_return_at = source.expected_return_at,
  target.return_at = source.return_at,
  target.borrow_status = source.borrow_status
WHEN NOT MATCHED THEN INSERT
  (borrow_id, material_id, club_id, borrower_user_id, quantity, borrow_at,
   expected_return_at, return_at, borrow_status)
VALUES
  (source.borrow_id, source.material_id, source.club_id, source.borrower_user_id,
   source.quantity, source.borrow_at, source.expected_return_at,
   source.return_at, source.borrow_status);

COMMIT;
