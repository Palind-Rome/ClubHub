-- 完整学生业务旅程样例：依赖基础用户、社团、组织架构、活动、学习中心与评奖评优样例。
-- 通过学工号和业务自然键定位记录，可重复执行；不会删除未知数据或重置账号密码。
-- 覆盖账号档案、当前成员任期、社团角色、活动参与、学习记录、评奖归档与成员考核。

SET DEFINE OFF;
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  v_required_count NUMBER;
BEGIN
  SELECT COUNT(*)
  INTO v_required_count
  FROM (
    SELECT 1 FROM USERS WHERE student_no = '2450001'
    UNION ALL SELECT 1 FROM USERS WHERE student_no = '06026'
    UNION ALL SELECT 1 FROM CLUBS WHERE club_id = 1
    UNION ALL SELECT 1 FROM ROLES WHERE role_code = 'CLUB_MEMBER'
    UNION ALL SELECT 1 FROM LEARNING_ITEMS WHERE item_id IN (1, 3, 10) HAVING COUNT(*) = 3
    UNION ALL SELECT 1 FROM ACTIVITIES WHERE activity_id = 7 AND club_id = 1
    UNION ALL SELECT 1 FROM AWARD_SCHEMES WHERE award_scheme_id = 137001 AND club_id = 1
    UNION ALL SELECT 1 FROM AWARD_LEVELS WHERE award_level_id = 137013 AND award_scheme_id = 137001
    UNION ALL SELECT 1 FROM AWARD_PUBLICITY_BATCHES WHERE publicity_batch_id = 137301 AND club_id = 1
  );

  IF v_required_count <> 9 THEN
    RAISE_APPLICATION_ERROR(-20190, 'Student journey seed prerequisites are incomplete.');
  END IF;
END;
/

UPDATE USERS
SET college = '计算机科学与技术学院',
    major = '软件工程',
    grade = '2024',
    updated_at = SYSDATE
WHERE student_no = '2450001';

UPDATE USERS
SET real_name = '贾雨村',
    college = '计算机科学与技术学院',
    major = '指导教师',
    grade = '教师',
    updated_at = SYSDATE
WHERE student_no = '06026';

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 138001 AS member_id,
         u.user_id,
         d.department_id,
         g.group_id
  FROM USERS u
  JOIN CLUB_DEPARTMENTS d ON d.club_id = 1 AND d.department_name = '技术部'
  JOIN CLUB_GROUPS g ON g.club_id = d.club_id
                    AND g.department_id = d.department_id
                    AND g.group_name = '开发组'
  WHERE u.student_no = '2450001'
) source
ON (
  target.club_id = 1
  AND target.user_id = source.user_id
  AND target.term_name = '2026-2027学年'
)
WHEN MATCHED THEN UPDATE SET
  target.department_id = source.department_id,
  target.group_id = source.group_id,
  target.department_name = '技术部',
  target.group_name = '开发组',
  target.position_name = '社员',
  target.term_start = DATE '2026-07-01',
  target.term_end = DATE '2027-06-30',
  target.member_status = 'active',
  target.join_at = DATE '2026-07-01',
  target.contribution_score = 86
WHEN NOT MATCHED THEN INSERT (
  member_id, club_id, user_id, department_id, group_id, department_name, group_name,
  position_name, term_name, term_start, term_end, member_status, join_at, contribution_score
) VALUES (
  source.member_id, 1, source.user_id, source.department_id, source.group_id, '技术部', '开发组',
  '社员', '2026-2027学年', DATE '2026-07-01', DATE '2027-06-30', 'active', DATE '2026-07-01', 86
);

MERGE INTO USER_ROLES target
USING (
  SELECT 138002 AS user_role_id, u.user_id, r.role_id
  FROM USERS u
  CROSS JOIN ROLES r
  WHERE u.student_no = '2450001'
    AND r.role_code = 'CLUB_MEMBER'
) source
ON (target.user_id = source.user_id AND target.role_id = source.role_id AND target.club_id = 1)
WHEN MATCHED THEN UPDATE SET target.assigned_at = DATE '2026-07-01'
WHEN NOT MATCHED THEN INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
VALUES (source.user_role_id, source.user_id, source.role_id, 1, DATE '2026-07-01');

MERGE INTO LEARNING_RECORDS target
USING (
  SELECT 138011 AS record_id, 1 AS item_id, u.user_id, 'completed' AS enroll_status,
         DATE '2026-03-02' AS enrolled_at, 100 AS progress, 10800 AS duration_seconds,
         DATE '2026-03-20' AS last_learn_at, DATE '2026-03-20' AS completed_at,
         CAST(NULL AS DATE) AS downloaded_at
  FROM USERS u WHERE u.student_no = '2450001'
  UNION ALL
  SELECT 138012, 3, u.user_id, 'learning', DATE '2026-08-18', 68, 5400,
         DATE '2026-08-27', CAST(NULL AS DATE), CAST(NULL AS DATE)
  FROM USERS u WHERE u.student_no = '2450001'
  UNION ALL
  SELECT 138013, 10, u.user_id, 'completed', DATE '2026-08-20', 100, 1800,
         DATE '2026-08-20', DATE '2026-08-20', DATE '2026-08-20'
  FROM USERS u WHERE u.student_no = '2450001'
) source
ON (target.item_id = source.item_id AND target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.enroll_status = source.enroll_status,
  target.enrolled_at = source.enrolled_at,
  target.progress = source.progress,
  target.duration_seconds = source.duration_seconds,
  target.last_learn_at = source.last_learn_at,
  target.completed_at = source.completed_at,
  target.downloaded_at = source.downloaded_at
WHEN NOT MATCHED THEN INSERT (
  record_id, item_id, user_id, enroll_status, enrolled_at, progress, duration_seconds,
  last_learn_at, completed_at, downloaded_at
) VALUES (
  source.record_id, source.item_id, source.user_id, source.enroll_status, source.enrolled_at,
  source.progress, source.duration_seconds, source.last_learn_at,
  source.completed_at, source.downloaded_at
);

MERGE INTO ACTIVITY_PARTICIPATIONS target
USING (
  SELECT 138021 AS participation_id, 7 AS activity_id, u.user_id, DATE '2026-08-22' AS registered_at
  FROM USERS u WHERE u.student_no = '2450020'
  UNION ALL
  SELECT 138022, 7, u.user_id, DATE '2026-08-23'
  FROM USERS u WHERE u.student_no = '2350021'
) source
ON (target.activity_id = source.activity_id AND target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.register_status = 'accepted',
  target.registered_at = source.registered_at,
  target.sign_status = 'registered',
  target.remark = '线上报名'
WHEN NOT MATCHED THEN INSERT (
  participation_id, activity_id, user_id, register_status, registered_at, sign_status, remark
) VALUES (
  source.participation_id, source.activity_id, source.user_id, 'accepted', source.registered_at, 'registered', '线上报名'
);

MERGE INTO AWARD_APPLICATIONS target
USING (
  SELECT 138101 AS award_application_id,
         u.user_id AS applicant_user_id,
         (SELECT president_user_id FROM CLUBS WHERE club_id = 1) AS recommender_user_id
  FROM USERS u
  WHERE u.student_no = '2450001'
) source
ON (target.award_scheme_id = 137001 AND target.applicant_user_id = source.applicant_user_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = 1,
  target.award_level_id = 137013,
  target.recommender_user_id = source.recommender_user_id,
  target.submitter_user_id = source.applicant_user_id,
  target.application_type = 'self',
  target.application_reason = '林黛玉在上一任期协助维护 ClubHub 项目需求、测试记录和新成员开发文档，并持续参与技术分享与学习资料整理。',
  target.current_step = 'archived',
  target.application_status = 'archived',
  target.public_status = 'publicized',
  target.review_round = 1,
  target.final_award_score = 8,
  target.final_amount = 0,
  target.submitted_at = DATE '2026-03-05',
  target.approved_at = DATE '2026-03-16',
  target.publicized_at = DATE '2026-03-22',
  target.archived_at = DATE '2026-03-25',
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN INSERT (
  award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
  recommender_user_id, submitter_user_id, application_type, application_reason,
  current_step, application_status, public_status, review_round,
  final_award_score, final_amount, submitted_at, approved_at, publicized_at,
  archived_at, created_at, updated_at
) VALUES (
  source.award_application_id, 1, 137001, 137013, source.applicant_user_id,
  source.recommender_user_id, source.applicant_user_id, 'self',
  '林黛玉在上一任期协助维护 ClubHub 项目需求、测试记录和新成员开发文档，并持续参与技术分享与学习资料整理。',
  'archived', 'archived', 'publicized', 1,
  8, 0, DATE '2026-03-05', DATE '2026-03-16', DATE '2026-03-22',
  DATE '2026-03-25', DATE '2026-03-05', SYSDATE
);

DELETE FROM AWARD_REVIEW_RECORDS
WHERE award_application_id = (
  SELECT aa.award_application_id
  FROM AWARD_APPLICATIONS aa
  JOIN USERS u ON u.user_id = aa.applicant_user_id
  WHERE aa.award_scheme_id = 137001 AND u.student_no = '2450001'
);

INSERT ALL
  INTO AWARD_REVIEW_RECORDS (
    review_id, award_application_id, review_round, review_step, review_result,
    reviewer_user_id, review_comment, from_status, to_status, reviewed_at
  ) VALUES (138201, award_application_id, 1, 'student_submit', 'submit', student_user_id,
            '提交优秀社员申请。', 'draft', 'club_review', DATE '2026-03-05')
  INTO AWARD_REVIEW_RECORDS (
    review_id, award_application_id, review_round, review_step, review_result,
    reviewer_user_id, review_comment, from_status, to_status, reviewed_at
  ) VALUES (138202, award_application_id, 1, 'club_review', 'approve', leader_user_id,
            '项目协作和资料整理记录完整，同意推荐优秀社员。', 'club_review', 'advisor_review', DATE '2026-03-10')
  INTO AWARD_REVIEW_RECORDS (
    review_id, award_application_id, review_round, review_step, review_result,
    reviewer_user_id, review_comment, from_status, to_status, reviewed_at
  ) VALUES (138203, award_application_id, 1, 'advisor_review', 'approve', advisor_user_id,
            '贡献记录清晰，同意通过。', 'advisor_review', 'school_review', DATE '2026-03-13')
  INTO AWARD_REVIEW_RECORDS (
    review_id, award_application_id, review_round, review_step, review_result,
    reviewer_user_id, review_comment, from_status, to_status, reviewed_at
  ) VALUES (138204, award_application_id, 1, 'school_review', 'approve', teacher_user_id,
            '复核通过，进入公示。', 'school_review', 'approved', DATE '2026-03-16')
  INTO AWARD_REVIEW_RECORDS (
    review_id, award_application_id, review_round, review_step, review_result,
    reviewer_user_id, review_comment, from_status, to_status, reviewed_at
  ) VALUES (138205, award_application_id, 1, 'publicity', 'publish', leader_user_id,
            '公示期无异议。', 'approved', 'publicized', DATE '2026-03-22')
  INTO AWARD_REVIEW_RECORDS (
    review_id, award_application_id, review_round, review_step, review_result,
    reviewer_user_id, review_comment, from_status, to_status, reviewed_at
  ) VALUES (138206, award_application_id, 1, 'archive', 'archive', leader_user_id,
            '完成归档并计入成员考核。', 'publicized', 'archived', DATE '2026-03-25')
SELECT aa.award_application_id,
       student.user_id AS student_user_id,
       leader.user_id AS leader_user_id,
       advisor.user_id AS advisor_user_id,
       teacher.user_id AS teacher_user_id
FROM AWARD_APPLICATIONS aa
JOIN USERS student ON student.user_id = aa.applicant_user_id AND student.student_no = '2450001'
CROSS JOIN (
  SELECT MIN(ur.user_id) AS user_id
  FROM USER_ROLES ur JOIN ROLES r ON r.role_id = ur.role_id
  WHERE ur.club_id = 1 AND r.role_code = 'CLUB_LEADER'
) leader
CROSS JOIN (
  SELECT MIN(ur.user_id) AS user_id
  FROM USER_ROLES ur JOIN ROLES r ON r.role_id = ur.role_id
  WHERE ur.club_id = 1 AND r.role_code = 'ADVISOR'
) advisor
CROSS JOIN (SELECT user_id FROM USERS WHERE student_no = '06026') teacher
WHERE aa.award_scheme_id = 137001;

MERGE INTO AWARD_PUBLICITY_ITEMS target
USING (
  SELECT 138301 AS publicity_item_id, aa.award_application_id
  FROM AWARD_APPLICATIONS aa
  JOIN USERS u ON u.user_id = aa.applicant_user_id
  WHERE aa.award_scheme_id = 137001 AND u.student_no = '2450001'
) source
ON (target.publicity_batch_id = 137301 AND target.award_application_id = source.award_application_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = 1,
  target.display_order = 30,
  target.publicity_result = 'normal'
WHEN NOT MATCHED THEN INSERT (
  publicity_item_id, publicity_batch_id, club_id, award_application_id, display_order, publicity_result, created_at
) VALUES (source.publicity_item_id, 137301, 1, source.award_application_id, 30, 'normal', SYSDATE);

MERGE INTO EVALUATIONS target
USING (
  SELECT 138401 AS evaluation_id,
         u.user_id,
         (SELECT MIN(ur.user_id)
          FROM USER_ROLES ur JOIN ROLES r ON r.role_id = ur.role_id
          WHERE ur.club_id = 1 AND r.role_code = 'CLUB_LEADER') AS evaluator_user_id
  FROM USERS u
  WHERE u.student_no = '2450001'
) source
ON (
  target.club_id = 1
  AND target.user_id = source.user_id
  AND target.evaluation_type = 'semester'
  AND target.term_name = '2025-2026学年春季'
)
WHEN MATCHED THEN UPDATE SET
  target.evaluator_user_id = source.evaluator_user_id,
  target.activity_score = 86,
  target.task_score = 88,
  target.learning_score = 90,
  target.award_score = 8,
  target.total_score = 272,
  target.grade = '优秀',
  target.public_status = 'published',
  target.comment_text = '上一任期工作认真，项目协作与资料沉淀表现突出。',
  target.created_at = DATE '2026-03-26'
WHEN NOT MATCHED THEN INSERT (
  evaluation_id, evaluation_type, club_id, user_id, evaluator_user_id, term_name,
  activity_score, task_score, learning_score, award_score, total_score,
  grade, public_status, comment_text, created_at
) VALUES (
  source.evaluation_id, 'semester', 1, source.user_id, source.evaluator_user_id, '2025-2026学年春季',
  86, 88, 90, 8, 272, '优秀', 'published',
  '上一任期工作认真，项目协作与资料沉淀表现突出。', DATE '2026-03-26'
);

MERGE INTO EVALUATION_AWARD_SOURCES target
USING (
  SELECT 1 AS club_id,
         u.user_id,
         e.evaluation_id,
         aa.award_application_id,
         8 AS award_score
  FROM USERS u
  JOIN EVALUATIONS e ON e.user_id = u.user_id
                    AND e.club_id = 1
                    AND e.evaluation_type = 'semester'
                    AND e.term_name = '2025-2026学年春季'
  JOIN AWARD_APPLICATIONS aa ON aa.applicant_user_id = u.user_id
                            AND aa.award_scheme_id = 137001
  WHERE u.student_no = '2450001'
) source
ON (
  target.evaluation_id = source.evaluation_id
  AND target.award_application_id = source.award_application_id
)
WHEN MATCHED THEN UPDATE SET target.award_score = source.award_score
WHEN NOT MATCHED THEN INSERT (
  club_id, user_id, evaluation_id, award_application_id, award_score, created_at
) VALUES (
  source.club_id, source.user_id, source.evaluation_id,
  source.award_application_id, source.award_score, SYSDATE
);

COMMIT;
