-- 评奖评优流程样例：依赖 000_sample_users.sql、001_sample_clubs.sql、
-- 005_sample_member_terms.sql、006_sample_club_organizations.sql 与
-- migrations/20260717_add_award_application_workflow.sql。
-- 围绕 zhang_guoxiong（张国雄）账号准备本人申请、负责人审核、公示归档和考核奖项分来源数据。

SET DEFINE OFF;

DECLARE
  v_zhang_count NUMBER;
BEGIN
  SELECT COUNT(*)
  INTO v_zhang_count
  FROM USERS
  WHERE username = 'zhang_guoxiong';

  IF v_zhang_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20140, 'Sample award workflow seed requires user zhang_guoxiong.');
  END IF;
END;
/

DELETE FROM EVALUATION_AWARD_SOURCES
WHERE evaluation_id BETWEEN 137500 AND 137599
   OR award_application_id BETWEEN 137100 AND 137199;

DELETE FROM AWARD_PUBLICITY_ITEMS
WHERE publicity_item_id BETWEEN 137400 AND 137499
   OR award_application_id BETWEEN 137100 AND 137199
   OR publicity_batch_id BETWEEN 137300 AND 137399;

DELETE FROM AWARD_PUBLICITY_BATCHES
WHERE publicity_batch_id BETWEEN 137300 AND 137399;

DELETE FROM AWARD_ATTACHMENTS
WHERE attachment_id BETWEEN 137150 AND 137199
   OR award_application_id BETWEEN 137100 AND 137199;

DELETE FROM AWARD_REVIEW_RECORDS
WHERE review_id BETWEEN 137200 AND 137299
   OR award_application_id BETWEEN 137100 AND 137199;

DELETE FROM AWARD_APPLICATIONS
WHERE award_application_id BETWEEN 137100 AND 137199
   OR award_scheme_id BETWEEN 137000 AND 137099;

DELETE FROM AWARD_LEVELS
WHERE award_level_id BETWEEN 137010 AND 137099
   OR award_scheme_id BETWEEN 137000 AND 137099;

DELETE FROM AWARD_SCHEMES
WHERE award_scheme_id BETWEEN 137000 AND 137099;

DELETE FROM EVALUATIONS
WHERE evaluation_id BETWEEN 137500 AND 137599;

INSERT ALL
  INTO AWARD_SCHEMES (
    award_scheme_id, club_id, award_name, award_category, academic_year, term_name,
    sponsor_unit, reward_level, funding_source, is_ranked, is_fixed_amount,
    description, material_description, application_start_at, application_end_at,
    publicity_start_at, publicity_end_at, scheme_status, created_by_user_id,
    created_at, updated_at
  )
  VALUES (
    137001, 1, '2025-2026学年春季优秀社员评选', 'honor', '2025-2026', '春季',
    '计算机协会', '社团级', '社团发展经费', 1, 1,
    '面向计算机协会上一学期在技术分享、竞赛服务和项目协作中表现突出的成员。',
    '个人总结、活动服务记录、竞赛或项目证明材料。',
    DATE '2026-03-01',
    DATE '2026-03-08',
    DATE '2026-03-18',
    DATE '2026-03-21',
    'archived', comp_president_id, SYSDATE, SYSDATE
  )
  INTO AWARD_SCHEMES (
    award_scheme_id, club_id, award_name, award_category, academic_year, term_name,
    sponsor_unit, reward_level, funding_source, is_ranked, is_fixed_amount,
    description, material_description, application_start_at, application_end_at,
    publicity_start_at, publicity_end_at, scheme_status, created_by_user_id,
    created_at, updated_at
  )
  VALUES (
    137002, 2, '2026年度校园影像贡献奖', 'service', '2026', '春季',
    '摄影社', '社团级', '社团活动经费', 1, 1,
    '表彰在校园影像记录、影展策划和作品整理中持续贡献的摄影社成员。',
    '作品集链接、活动跟拍排班、影展布置或后期交付证明。',
    DATE '2026-07-10',
    DATE '2026-07-22',
    DATE '2026-07-28',
    DATE '2026-07-31',
    'reviewing', photo_president_id, SYSDATE, SYSDATE
  )
  INTO AWARD_SCHEMES (
    award_scheme_id, club_id, award_name, award_category, academic_year, term_name,
    sponsor_unit, reward_level, funding_source, is_ranked, is_fixed_amount,
    description, material_description, application_start_at, application_end_at,
    publicity_start_at, publicity_end_at, scheme_status, created_by_user_id,
    created_at, updated_at
  )
  VALUES (
    137003, 3, '2026春季训练贡献奖', 'service', '2025-2026', '春季',
    '羽毛球协会', '社团级', '社团训练经费', 1, 0,
    '面向春季训练、校内联赛和新人带练中表现突出的羽毛球协会成员。',
    '训练出勤、赛事服务记录、带练反馈或比赛成绩证明。',
    DATE '2026-06-20',
    DATE '2026-07-05',
    DATE '2026-07-15',
    DATE '2026-07-20',
    'publicizing', zhang_id, SYSDATE, SYSDATE
  )
  INTO AWARD_SCHEMES (
    award_scheme_id, club_id, award_name, award_category, academic_year, term_name,
    sponsor_unit, reward_level, funding_source, is_ranked, is_fixed_amount,
    description, material_description, application_start_at, application_end_at,
    publicity_start_at, publicity_end_at, scheme_status, created_by_user_id,
    created_at, updated_at
  )
  VALUES (
    137004, 3, '2025-2026学年优秀干部评定', 'honor', '2025-2026', '学年',
    '羽毛球协会', '社团级', '无固定金额', 1, 0,
    '面向上一学年在协会管理、训练组织和赛事统筹中承担核心职责的干部。',
    '任期总结、训练计划、赛事组织材料和成员反馈。',
    DATE '2026-06-01',
    DATE '2026-06-10',
    DATE '2026-06-18',
    DATE '2026-06-21',
    'archived', zhang_id, SYSDATE, SYSDATE
  )
SELECT (SELECT MAX(user_id) FROM USERS WHERE username = 'zhang_guoxiong') AS zhang_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 1) AS comp_president_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 2) AS photo_president_id
FROM dual;

INSERT ALL
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137011, 137001, '一等奖', 18, 600, 1, 10, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137012, 137001, '二等奖', 12, 300, 3, 20, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137013, 137001, '优秀社员', 8, 0, 6, 30, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137021, 137002, '金镜头', 16, 500, 1, 10, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137022, 137002, '银镜头', 10, 300, 2, 20, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137023, 137002, '入围作品', 6, 0, 8, 30, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137031, 137003, '卓越贡献', 15, NULL, 2, 10, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137032, 137003, '训练标兵', 10, NULL, 5, 20, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137033, 137003, '服务之星', 6, NULL, 8, 30, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137041, 137004, '优秀负责人', 15, NULL, 1, 10, 'active', SYSDATE, SYSDATE)
  INTO AWARD_LEVELS (award_level_id, award_scheme_id, level_name, award_score, amount, quota, display_order, level_status, created_at, updated_at)
  VALUES (137042, 137004, '优秀部长', 10, NULL, 3, 20, 'active', SYSDATE, SYSDATE)
SELECT 1 FROM dual;

INSERT ALL
  INTO AWARD_APPLICATIONS (
    award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
    recommender_user_id, submitter_user_id, application_type, application_reason,
    material_url, current_step, application_status, public_status, review_round,
    final_award_score, final_amount, submitted_at, approved_at, publicized_at,
    archived_at, created_at, updated_at
  )
  VALUES (
    137101, 1, 137001, 137011, zhang_id,
    NULL, zhang_id, 'self', '张国雄在算法部训练组承担题单维护和周赛讲解，协助完成新成员训练营和校赛志愿服务。',
    '/demo/awards/zhang-guoxiong-computer-2026.pdf', 'archived', 'archived', 'publicized', 1,
    18, 600, DATE '2026-03-04',
    DATE '2026-03-16',
    DATE '2026-03-22',
    DATE '2026-03-25',
    DATE '2026-03-04', SYSDATE
  )
  INTO AWARD_APPLICATIONS (
    award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
    recommender_user_id, submitter_user_id, application_type, application_reason,
    material_url, current_step, application_status, public_status, review_round,
    final_award_score, final_amount, submitted_at, approved_at, publicized_at,
    archived_at, created_at, updated_at
  )
  VALUES (
    137102, 1, 137001, 137012, 20,
    comp_president_id, comp_president_id, 'recommendation', '赵睿参与后端工具维护和数据库脚本整理，春季训练营中负责答疑值班。',
    '/demo/awards/zhao-rui-computer-2026.pdf', 'archived', 'archived', 'publicized', 1,
    12, 300, DATE '2026-03-05',
    DATE '2026-03-16',
    DATE '2026-03-22',
    DATE '2026-03-25',
    DATE '2026-03-05', SYSDATE
  )
  INTO AWARD_APPLICATIONS (
    award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
    recommender_user_id, submitter_user_id, application_type, application_reason,
    material_url, current_step, application_status, public_status, review_round,
    final_award_score, final_amount, submitted_at, approved_at, publicized_at,
    archived_at, created_at, updated_at
  )
  VALUES (
    137103, 2, 137002, 137021, zhang_id,
    NULL, zhang_id, 'self', '张国雄作为摄影社活动部部长，负责暑期校园影像征集排期、作品初筛和影展现场协调。',
    '/demo/awards/zhang-guoxiong-photo-portfolio.txt', 'advisor_review', 'advisor_review', 'none', 1,
    NULL, NULL, DATE '2026-07-12',
    NULL, NULL, NULL, DATE '2026-07-12', SYSDATE
  )
  INTO AWARD_APPLICATIONS (
    award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
    recommender_user_id, submitter_user_id, application_type, application_reason,
    material_url, current_step, application_status, public_status, review_round,
    final_award_score, final_amount, submitted_at, approved_at, publicized_at,
    archived_at, created_at, updated_at
  )
  VALUES (
    137104, 3, 137003, 137031, 28,
    zhang_id, zhang_id, 'recommendation', '沈一鸣负责校队训练组日常训练、对抗赛复盘和新人陪练，建议授予卓越贡献。',
    '/demo/awards/shen-yiming-training.xlsx', 'club_review', 'club_review', 'none', 1,
    NULL, NULL, DATE '2026-07-13',
    NULL, NULL, NULL, DATE '2026-07-13', SYSDATE
  )
  INTO AWARD_APPLICATIONS (
    award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
    recommender_user_id, submitter_user_id, application_type, application_reason,
    material_url, current_step, application_status, public_status, review_round,
    final_award_score, final_amount, submitted_at, approved_at, publicized_at,
    archived_at, created_at, updated_at
  )
  VALUES (
    137105, 3, 137003, 137032, 29,
    zhang_id, zhang_id, 'recommendation', '叶清扬在春季联赛中承担裁判和计分工作，协助整理赛程与申诉记录。',
    '/demo/awards/ye-qingyang-referee.pdf', 'publicity', 'publicizing', 'publicizing', 1,
    10, NULL, DATE '2026-07-03',
    DATE '2026-07-14',
    NULL, NULL, DATE '2026-07-03', SYSDATE
  )
  INTO AWARD_APPLICATIONS (
    award_application_id, club_id, award_scheme_id, award_level_id, applicant_user_id,
    recommender_user_id, submitter_user_id, application_type, application_reason,
    material_url, current_step, application_status, public_status, review_round,
    final_award_score, final_amount, submitted_at, approved_at, publicized_at,
    archived_at, created_at, updated_at
  )
  VALUES (
    137106, 3, 137004, 137041, zhang_id,
    NULL, zhang_id, 'self', '张国雄上一学年担任校队训练组队长，组织周训、队内赛和新老成员交接，任期评价优秀。',
    '/demo/awards/zhang-guoxiong-badminton-leader.pdf', 'archived', 'archived', 'publicized', 1,
    15, NULL, DATE '2026-06-04',
    DATE '2026-06-16',
    DATE '2026-06-22',
    DATE '2026-06-25',
    DATE '2026-06-04', SYSDATE
  )
SELECT (SELECT MAX(user_id) FROM USERS WHERE username = 'zhang_guoxiong') AS zhang_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 1) AS comp_president_id
FROM dual;

INSERT ALL
  INTO AWARD_ATTACHMENTS (attachment_id, award_application_id, attachment_name, attachment_url, attachment_type, uploaded_by_user_id, uploaded_at)
  VALUES (137151, 137101, '计算机协会服务与竞赛证明.pdf', '/demo/awards/zhang-guoxiong-computer-2026.pdf', 'proof', zhang_id, DATE '2026-03-04')
  INTO AWARD_ATTACHMENTS (attachment_id, award_application_id, attachment_name, attachment_url, attachment_type, uploaded_by_user_id, uploaded_at)
  VALUES (137152, 137103, '摄影作品集与影展排期.txt', '/demo/awards/zhang-guoxiong-photo-portfolio.txt', 'portfolio', zhang_id, DATE '2026-07-12')
  INTO AWARD_ATTACHMENTS (attachment_id, award_application_id, attachment_name, attachment_url, attachment_type, uploaded_by_user_id, uploaded_at)
  VALUES (137153, 137104, '春季训练出勤统计.xlsx', '/demo/awards/shen-yiming-training.xlsx', 'statistic', zhang_id, DATE '2026-07-13')
  INTO AWARD_ATTACHMENTS (attachment_id, award_application_id, attachment_name, attachment_url, attachment_type, uploaded_by_user_id, uploaded_at)
  VALUES (137154, 137106, '羽协干部任期总结.pdf', '/demo/awards/zhang-guoxiong-badminton-leader.pdf', 'summary', zhang_id, DATE '2026-06-04')
SELECT (SELECT MAX(user_id) FROM USERS WHERE username = 'zhang_guoxiong') AS zhang_id
FROM dual;

INSERT ALL
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137201, 137101, 1, 'student_submit', 'submit', zhang_id, '提交优秀社员申请。', 'draft', 'club_review', DATE '2026-03-04')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137202, 137101, 1, 'club_review', 'approve', comp_president_id, '材料完整，算法部服务记录清晰，同意推荐一等奖。', 'club_review', 'advisor_review', DATE '2026-03-10')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137203, 137101, 1, 'advisor_review', 'approve', comp_advisor_id, '贡献突出，建议通过。', 'advisor_review', 'school_review', DATE '2026-03-13')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137204, 137101, 1, 'school_review', 'approve', school_reviewer_id, '校级复核通过，进入公示。', 'school_review', 'approved', DATE '2026-03-16')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137205, 137101, 1, 'publicity', 'publish', comp_president_id, '公示期无异议。', 'approved', 'publicized', DATE '2026-03-22')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137206, 137101, 1, 'archive', 'archive', comp_president_id, '归档并同步为成员考核奖项分。', 'publicized', 'archived', DATE '2026-03-25')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137207, 137102, 1, 'student_submit', 'submit', comp_president_id, '负责人推荐赵睿参评。', 'draft', 'club_review', DATE '2026-03-05')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137208, 137102, 1, 'club_review', 'approve', comp_president_id, '后端组贡献稳定，同意推荐二等奖。', 'club_review', 'advisor_review', DATE '2026-03-10')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137209, 137102, 1, 'advisor_review', 'approve', comp_advisor_id, '同意通过。', 'advisor_review', 'school_review', DATE '2026-03-13')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137210, 137102, 1, 'school_review', 'approve', school_reviewer_id, '复核通过。', 'school_review', 'approved', DATE '2026-03-16')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137211, 137102, 1, 'archive', 'archive', comp_president_id, '公示无异议，完成归档。', 'publicized', 'archived', DATE '2026-03-25')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137212, 137103, 1, 'student_submit', 'submit', zhang_id, '提交校园影像贡献奖申请。', 'draft', 'club_review', DATE '2026-07-12')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137213, 137103, 1, 'club_review', 'approve', photo_president_id, '作品集完整，活动部排期贡献明显，提交指导老师审核。', 'club_review', 'advisor_review', DATE '2026-07-15')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137214, 137104, 1, 'student_submit', 'submit', zhang_id, '张国雄以负责人身份推荐沈一鸣参评。', 'draft', 'club_review', DATE '2026-07-13')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137215, 137105, 1, 'student_submit', 'submit', zhang_id, '推荐叶清扬参评训练标兵。', 'draft', 'club_review', DATE '2026-07-03')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137216, 137105, 1, 'club_review', 'approve', zhang_id, '赛事部服务记录完整，同意进入校级复核。', 'club_review', 'school_review', DATE '2026-07-08')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137217, 137105, 1, 'school_review', 'approve', school_reviewer_id, '复核通过，进入公示。', 'school_review', 'approved', DATE '2026-07-14')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137218, 137105, 1, 'publicity', 'publish', zhang_id, '已发布公示，等待异议期结束。', 'approved', 'publicizing', DATE '2026-07-15')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137219, 137106, 1, 'student_submit', 'submit', zhang_id, '提交优秀干部自评材料。', 'draft', 'club_review', DATE '2026-06-04')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137220, 137106, 1, 'club_review', 'approve', school_reviewer_id, '负责人任期材料由社团管理员复核通过。', 'club_review', 'school_review', DATE '2026-06-12')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137221, 137106, 1, 'school_review', 'approve', school_reviewer_id, '校级复核通过。', 'school_review', 'approved', DATE '2026-06-16')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137222, 137106, 1, 'publicity', 'publish', zhang_id, '优秀干部名单公示无异议。', 'approved', 'publicized', DATE '2026-06-22')
  INTO AWARD_REVIEW_RECORDS (review_id, award_application_id, review_round, review_step, review_result, reviewer_user_id, review_comment, from_status, to_status, reviewed_at)
  VALUES (137223, 137106, 1, 'archive', 'archive', zhang_id, '归档并同步至学年考核奖项分。', 'publicized', 'archived', DATE '2026-06-25')
SELECT (SELECT MAX(user_id) FROM USERS WHERE username = 'zhang_guoxiong') AS zhang_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 1) AS comp_president_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 2) AS photo_president_id,
       COALESCE(
         (SELECT MAX(user_id) FROM USERS WHERE username = 'advisor_zhang'),
         (SELECT MAX(user_id) FROM USERS WHERE username = 'teacher_advisor')
       ) AS comp_advisor_id,
       COALESCE((SELECT MAX(user_id) FROM USERS WHERE username = 'admin_li'), 2) AS school_reviewer_id
FROM dual;

INSERT ALL
  INTO AWARD_PUBLICITY_BATCHES (
    publicity_batch_id, club_id, title, description, publicity_start_at,
    publicity_end_at, publicity_status, publisher_user_id, created_at, updated_at
  )
  VALUES (
    137301, 1, '计算机协会2025-2026春季优秀社员公示',
    '公示张国雄、赵睿等成员春季优秀社员评选结果。',
    DATE '2026-03-18',
    DATE '2026-03-21',
    'archived', comp_president_id, SYSDATE, SYSDATE
  )
  INTO AWARD_PUBLICITY_BATCHES (
    publicity_batch_id, club_id, title, description, publicity_start_at,
    publicity_end_at, publicity_status, publisher_user_id, created_at, updated_at
  )
  VALUES (
    137302, 3, '羽毛球协会2025-2026优秀干部公示',
    '公示张国雄上一学年优秀干部评定结果。',
    DATE '2026-06-18',
    DATE '2026-06-21',
    'archived', zhang_id, SYSDATE, SYSDATE
  )
  INTO AWARD_PUBLICITY_BATCHES (
    publicity_batch_id, club_id, title, description, publicity_start_at,
    publicity_end_at, publicity_status, publisher_user_id, created_at, updated_at
  )
  VALUES (
    137303, 3, '羽毛球协会2026春季训练贡献奖公示',
    '公示春季训练贡献奖拟获奖名单，当前仍在异议期。',
    DATE '2026-07-15',
    DATE '2026-07-20',
    'publicizing', zhang_id, SYSDATE, SYSDATE
  )
SELECT (SELECT MAX(user_id) FROM USERS WHERE username = 'zhang_guoxiong') AS zhang_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 1) AS comp_president_id
FROM dual;

INSERT ALL
  INTO AWARD_PUBLICITY_ITEMS (publicity_item_id, publicity_batch_id, club_id, award_application_id, display_order, publicity_result, created_at)
  VALUES (137401, 137301, 1, 137101, 10, 'normal', SYSDATE)
  INTO AWARD_PUBLICITY_ITEMS (publicity_item_id, publicity_batch_id, club_id, award_application_id, display_order, publicity_result, created_at)
  VALUES (137402, 137301, 1, 137102, 20, 'normal', SYSDATE)
  INTO AWARD_PUBLICITY_ITEMS (publicity_item_id, publicity_batch_id, club_id, award_application_id, display_order, publicity_result, created_at)
  VALUES (137403, 137302, 3, 137106, 10, 'normal', SYSDATE)
  INTO AWARD_PUBLICITY_ITEMS (publicity_item_id, publicity_batch_id, club_id, award_application_id, display_order, publicity_result, created_at)
  VALUES (137404, 137303, 3, 137105, 10, 'normal', SYSDATE)
SELECT 1 FROM dual;

INSERT ALL
  INTO EVALUATIONS (
    evaluation_id, evaluation_type, club_id, user_id, evaluator_user_id, term_name,
    award_title, award_level, award_reason, activity_score, task_score,
    learning_score, award_score, total_score, grade, public_status, comment_text, created_at
  )
  VALUES (
    137501, 'semester', 1, zhang_id, comp_president_id, '2025-2026学年春季',
    NULL, NULL, NULL, 88, 90, 92, 18, 288, '优秀', 'published',
    '奖项分来自计算机协会春季优秀社员评选一等奖。', DATE '2026-03-26'
  )
  INTO EVALUATIONS (
    evaluation_id, evaluation_type, club_id, user_id, evaluator_user_id, term_name,
    award_title, award_level, award_reason, activity_score, task_score,
    learning_score, award_score, total_score, grade, public_status, comment_text, created_at
  )
  VALUES (
    137502, 'semester', 3, zhang_id, school_reviewer_id, '2025-2026学年',
    NULL, NULL, NULL, 93, 95, 90, 15, 293, '优秀', 'published',
    '奖项分来自羽毛球协会优秀干部评定优秀负责人。', DATE '2026-06-26'
  )
SELECT (SELECT MAX(user_id) FROM USERS WHERE username = 'zhang_guoxiong') AS zhang_id,
       (SELECT president_user_id FROM CLUBS WHERE club_id = 1) AS comp_president_id,
       COALESCE((SELECT MAX(user_id) FROM USERS WHERE username = 'admin_li'), 2) AS school_reviewer_id
FROM dual;

INSERT ALL
  INTO EVALUATION_AWARD_SOURCES (evaluation_id, award_application_id, award_score, created_at)
  VALUES (137501, 137101, 18, SYSDATE)
  INTO EVALUATION_AWARD_SOURCES (evaluation_id, award_application_id, award_score, created_at)
  VALUES (137502, 137106, 15, SYSDATE)
SELECT 1 FROM dual;

DECLARE
  v_invalid_award_links NUMBER;
  v_invalid_source_links NUMBER;
BEGIN
  SELECT COUNT(*)
  INTO v_invalid_award_links
  FROM AWARD_APPLICATIONS application
  LEFT JOIN AWARD_SCHEMES scheme
    ON scheme.club_id = application.club_id
   AND scheme.award_scheme_id = application.award_scheme_id
  LEFT JOIN AWARD_LEVELS award_level
    ON award_level.award_scheme_id = application.award_scheme_id
   AND award_level.award_level_id = application.award_level_id
  WHERE application.award_application_id BETWEEN 137100 AND 137199
    AND (scheme.award_scheme_id IS NULL OR award_level.award_level_id IS NULL);

  IF v_invalid_award_links > 0 THEN
    RAISE_APPLICATION_ERROR(-20138, 'Sample award workflow seed left invalid award scheme or level references.');
  END IF;

  SELECT COUNT(*)
  INTO v_invalid_source_links
  FROM EVALUATION_AWARD_SOURCES source
  JOIN EVALUATIONS evaluation
    ON evaluation.evaluation_id = source.evaluation_id
  JOIN AWARD_APPLICATIONS application
    ON application.award_application_id = source.award_application_id
  WHERE source.evaluation_id BETWEEN 137500 AND 137599
    AND (
      evaluation.club_id <> application.club_id
      OR evaluation.user_id <> application.applicant_user_id
    );

  IF v_invalid_source_links > 0 THEN
    RAISE_APPLICATION_ERROR(-20139, 'Sample award workflow seed left invalid evaluation award source links.');
  END IF;
END;
/

COMMIT;
