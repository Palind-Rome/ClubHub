-- 在导入含显式主键的样例数据后，将现有主键 sequence 推进到表内最大值之后。
--
-- 背景：历史样例与早期手工数据曾使用 1000000 附近的显式 ID；若 sequence
-- 仍停留在同一区间，下一次正常写入会触发 ORA-00001。脚本只校正已经存在的
-- sequence，不创建表或 sequence，不修改、删除任何业务行。
--
-- 可重复执行：LAST_NUMBER 已大于最大主键时完全不操作。Oracle DDL 会隐式提交，
-- 应由 CLUBHUB schema 所有者在维护窗口执行；脚本末尾会输出关键序列的校验结果。
-- PR #167 对应的 IDEMPOTENCY_RECORDS 已明确不在当前部署范围，本脚本不创建也不
-- 校正 SEQ_IDEMPOTENCY_RECORDS。
--
-- 影响范围：仅限下方 sequence_names 中与业务主键对应的 sequence。执行前必须从
-- USER_SEQUENCES 记录每条目标 sequence 的 LAST_NUMBER、INCREMENT_BY、CACHE_SIZE、
-- MIN_VALUE、MAX_VALUE、CYCLE_FLAG 和 ORDER_FLAG，作为人工回退依据。若迁移后尚未发生
-- 任何业务写入，可依据记录恢复原配置和位置；一旦已有新写入，禁止把 sequence 向下调整，
-- 只能恢复 INCREMENT/CACHE/CYCLE/ORDER 等非位置属性，并从不与现有主键冲突的位置继续。
-- 本脚本对被推进的 sequence 最终使用 INCREMENT BY 1 NOCACHE NOCYCLE；NOCACHE 是
-- 当前目标配置，不应被监控或审计误判为迁移失败。由于 DDL 隐式提交，SQL ROLLBACK
-- 不能撤销 sequence 变更。

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  TYPE identifier_list IS TABLE OF VARCHAR2(128);

  table_names identifier_list := identifier_list(
    'USERS', 'USER_ROLES', 'CLUBS', 'CLUB_MEMBERS', 'CLUB_DEPARTMENTS', 'CLUB_GROUPS',
    'AWARD_SCHEMES', 'AWARD_LEVELS', 'AWARD_APPLICATIONS', 'AWARD_REVIEW_RECORDS',
    'AWARD_ATTACHMENTS', 'AWARD_PUBLICITY_BATCHES', 'AWARD_PUBLICITY_ITEMS',
    'AWARD_RULE_DOCUMENTS', 'EVALUATIONS', 'ACTIVITIES', 'ACTIVITY_PARTICIPATIONS',
    'BUDGET_ACCOUNTS', 'BUDGET_APPLICATIONS', 'BUDGET_REVIEW_RECORDS',
    'BUDGET_TRANSACTIONS', 'NOTICE_READS', 'ROLES', 'RECRUITMENTS',
    'RECRUITMENT_APPLICATIONS', 'VENUES', 'VENUE_RESERVATIONS', 'PROJECTS',
    'PROJECT_MEMBERS', 'PROJECT_TASKS', 'PROJECT_TASK_ASSIGNEES',
    'PROJECT_TASK_PROGRESS_REPORTS', 'LEARNING_ITEMS', 'LEARNING_RECORDS',
    'MATERIALS', 'MATERIAL_BORROWS', 'NOTICES', 'FORUM_POSTS', 'OPERATION_LOGS'
  );

  column_names identifier_list := identifier_list(
    'USER_ID', 'USER_ROLE_ID', 'CLUB_ID', 'MEMBER_ID', 'DEPARTMENT_ID', 'GROUP_ID',
    'AWARD_SCHEME_ID', 'AWARD_LEVEL_ID', 'AWARD_APPLICATION_ID', 'REVIEW_ID',
    'ATTACHMENT_ID', 'PUBLICITY_BATCH_ID', 'PUBLICITY_ITEM_ID', 'RULE_DOCUMENT_ID',
    'EVALUATION_ID', 'ACTIVITY_ID', 'PARTICIPATION_ID', 'ACCOUNT_ID', 'APPLICATION_ID',
    'REVIEW_ID', 'TRANSACTION_ID', 'READ_ID', 'ROLE_ID', 'RECRUIT_ID', 'APPLICATION_ID',
    'VENUE_ID', 'RESERVATION_ID', 'PROJECT_ID', 'PROJECT_MEMBER_ID', 'TASK_ID',
    'TASK_ASSIGNEE_ID', 'TASK_PROGRESS_REPORT_ID', 'ITEM_ID', 'RECORD_ID',
    'MATERIAL_ID', 'BORROW_ID', 'NOTICE_ID', 'POST_ID', 'LOG_ID'
  );

  sequence_names identifier_list := identifier_list(
    'SEQ_USERS', 'SEQ_USER_ROLES', 'SEQ_CLUBS', 'SEQ_CLUB_MEMBERS',
    'SEQ_CLUB_DEPARTMENTS', 'SEQ_CLUB_GROUPS', 'SEQ_AWARD_SCHEMES', 'SEQ_AWARD_LEVELS',
    'SEQ_AWARD_APPLICATIONS', 'SEQ_AWARD_REVIEW_RECORDS', 'SEQ_AWARD_ATTACHMENTS',
    'SEQ_AWARD_PUBLICITY_BATCHES', 'SEQ_AWARD_PUBLICITY_ITEMS',
    'SEQ_AWARD_RULE_DOCUMENTS', 'SEQ_EVALUATIONS', 'SEQ_ACTIVITIES',
    'SEQ_ACTIVITY_PARTICIPATIONS', 'SEQ_BUDGET_ACCOUNTS', 'SEQ_BUDGET_APPLICATIONS',
    'SEQ_BUDGET_REVIEW_RECORDS', 'SEQ_BUDGET_TRANSACTIONS', 'SEQ_NOTICE_READS',
    'SEQ_ROLES', 'SEQ_RECRUITMENTS', 'SEQ_RECRUITMENT_APPLICATIONS', 'SEQ_VENUES',
    'SEQ_VENUE_RESERVATIONS', 'SEQ_PROJECTS', 'SEQ_PROJECT_MEMBERS', 'SEQ_PROJECT_TASKS',
    'SEQ_PROJECT_TASK_ASSIGNEES', 'SEQ_PROJECT_TASK_PROGRESS_REPORTS',
    'SEQ_LEARNING_ITEMS', 'SEQ_LEARNING_RECORDS', 'SEQ_MATERIALS',
    'SEQ_MATERIAL_BORROWS', 'SEQ_NOTICES', 'SEQ_FORUM_POSTS', 'SEQ_OPERATION_LOGS'
  );

  max_id NUMBER;
  last_number NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  IF table_names.COUNT != column_names.COUNT OR
     table_names.COUNT != sequence_names.COUNT THEN
    RAISE_APPLICATION_ERROR(-20220, 'Sequence alignment target lists are inconsistent.');
  END IF;

  FOR item_index IN 1 .. table_names.COUNT LOOP
    EXECUTE IMMEDIATE
      'SELECT NVL(MAX(' || column_names(item_index) || '), 0) FROM ' ||
      table_names(item_index)
      INTO max_id;

    SELECT state.last_number
    INTO last_number
    FROM user_sequences state
    WHERE state.sequence_name = sequence_names(item_index);

    target_id := GREATEST(max_id + 1, 1000000);
    IF last_number < target_id THEN
      EXECUTE IMMEDIATE
        'SELECT ' || sequence_names(item_index) || '.NEXTVAL FROM dual'
        INTO sequence_value;
      EXECUTE IMMEDIATE
        'ALTER SEQUENCE ' || sequence_names(item_index) || ' INCREMENT BY ' ||
        TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
      EXECUTE IMMEDIATE
        'SELECT ' || sequence_names(item_index) || '.NEXTVAL FROM dual'
        INTO sequence_value;
      EXECUTE IMMEDIATE
        'ALTER SEQUENCE ' || sequence_names(item_index) ||
        ' INCREMENT BY 1 NOCACHE NOCYCLE';
    END IF;
  END LOOP;
END;
/

SELECT target.sequence_name, target.max_id, state.last_number
FROM (
  SELECT 'SEQ_USERS' sequence_name, NVL(MAX(user_id), 0) max_id FROM users
  UNION ALL SELECT 'SEQ_AWARD_REVIEW_RECORDS', NVL(MAX(review_id), 0) FROM award_review_records
  UNION ALL SELECT 'SEQ_FORUM_POSTS', NVL(MAX(post_id), 0) FROM forum_posts
  UNION ALL SELECT 'SEQ_OPERATION_LOGS', NVL(MAX(log_id), 0) FROM operation_logs
) target
JOIN user_sequences state ON state.sequence_name = target.sequence_name
ORDER BY target.sequence_name;
