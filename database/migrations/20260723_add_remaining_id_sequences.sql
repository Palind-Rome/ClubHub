-- Issue #150：淘汰剩余运行时 MAX(id) + 1 主键生成，统一使用 Oracle sequence。
--
-- 执行前（强制前置条件）：
--   1. 备份目标 schema。
--   2. 使用 CLUBHUB schema 所有者执行。
--   3. 确认目标是共享开发库或明确测试库；生产/演示库必须另行人工确认。
--   4. 进入维护窗口，停止所有仍使用 MAX(id) + 1 生成主键的旧后端实例，
--      直至本迁移和 verify.sql 均执行成功。
--
-- 影响范围：
--   - 新增 17 个 SEQ_<TABLE> sequence；
--   - 为对应主键列设置 sequence NEXTVAL 默认值；
--   - 不新增、删除或重命名表、列、主键、外键，不更新或删除任何业务行。
--
-- Oracle DDL 会隐式提交，不能依赖 ROLLBACK 撤销已生效的步骤。
-- 回滚准备：执行前导出目标列的 USER_TAB_COLUMNS.DATA_DEFAULT，以及目标 sequence
-- 在 USER_SEQUENCES 中的存在性、LAST_NUMBER、INCREMENT_BY、MIN_VALUE、MAX_VALUE、
-- CACHE_SIZE、CYCLE_FLAG 和 ORDER_FLAG。
-- 回滚：若迁移后尚无新写入，按导出结果恢复列默认值；仅删除本迁移新建的
-- sequence，原有 sequence 由 DBA 在维护窗口按备份恢复属性和位置。
-- 若迁移后已有新写入，不得删除或回退 sequence；应先评估已生成的主键，并由 DBA
-- 采用不会与现有主键冲突的调整方案，仅恢复安全的默认值或 sequence 属性。
-- 脚本可重复执行：
-- 已存在的 sequence 会被推进到实时最大主键之后且不低于 1000000，已设置的
-- 列默认值会被同值覆盖。若脚本中断，修复原因后可从头重跑。

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  TYPE identifier_list IS TABLE OF VARCHAR2(128);

  table_names identifier_list := identifier_list(
    'ROLES',
    'RECRUITMENTS',
    'RECRUITMENT_APPLICATIONS',
    'VENUES',
    'VENUE_RESERVATIONS',
    'PROJECTS',
    'PROJECT_MEMBERS',
    'PROJECT_TASKS',
    'PROJECT_TASK_ASSIGNEES',
    'PROJECT_TASK_PROGRESS_REPORTS',
    'LEARNING_ITEMS',
    'LEARNING_RECORDS',
    'MATERIALS',
    'MATERIAL_BORROWS',
    'NOTICES',
    'FORUM_POSTS',
    'OPERATION_LOGS'
  );

  column_names identifier_list := identifier_list(
    'ROLE_ID',
    'RECRUIT_ID',
    'APPLICATION_ID',
    'VENUE_ID',
    'RESERVATION_ID',
    'PROJECT_ID',
    'PROJECT_MEMBER_ID',
    'TASK_ID',
    'TASK_ASSIGNEE_ID',
    'TASK_PROGRESS_REPORT_ID',
    'ITEM_ID',
    'RECORD_ID',
    'MATERIAL_ID',
    'BORROW_ID',
    'NOTICE_ID',
    'POST_ID',
    'LOG_ID'
  );

  sequence_names identifier_list := identifier_list(
    'SEQ_ROLES',
    'SEQ_RECRUITMENTS',
    'SEQ_RECRUITMENT_APPLICATIONS',
    'SEQ_VENUES',
    'SEQ_VENUE_RESERVATIONS',
    'SEQ_PROJECTS',
    'SEQ_PROJECT_MEMBERS',
    'SEQ_PROJECT_TASKS',
    'SEQ_PROJECT_TASK_ASSIGNEES',
    'SEQ_PROJECT_TASK_PROGRESS_REPORTS',
    'SEQ_LEARNING_ITEMS',
    'SEQ_LEARNING_RECORDS',
    'SEQ_MATERIALS',
    'SEQ_MATERIAL_BORROWS',
    'SEQ_NOTICES',
    'SEQ_FORUM_POSTS',
    'SEQ_OPERATION_LOGS'
  );

  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  IF table_names.COUNT != column_names.COUNT OR
     table_names.COUNT != sequence_names.COUNT THEN
    RAISE_APPLICATION_ERROR(-20001, 'Sequence migration target lists are inconsistent.');
  END IF;

  FOR item_index IN 1 .. table_names.COUNT LOOP
    EXECUTE IMMEDIATE
      'SELECT NVL(MAX(' || column_names(item_index) || '), 0) FROM ' ||
      table_names(item_index)
      INTO max_id;

    target_id := GREATEST(max_id + 1, 1000000);

    BEGIN
      EXECUTE IMMEDIATE
        'CREATE SEQUENCE ' || sequence_names(item_index) ||
        ' START WITH ' || TO_CHAR(target_id, 'FM99999999999999999990') ||
        ' INCREMENT BY 1 NOCACHE NOCYCLE';
    EXCEPTION
      WHEN OTHERS THEN
        IF SQLCODE != -955 THEN
          RAISE;
        END IF;
    END;

    EXECUTE IMMEDIATE
      'SELECT ' || sequence_names(item_index) || '.NEXTVAL FROM dual'
      INTO sequence_value;

    IF sequence_value < target_id THEN
      EXECUTE IMMEDIATE
        'ALTER SEQUENCE ' || sequence_names(item_index) ||
        ' INCREMENT BY ' ||
        TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
      EXECUTE IMMEDIATE
        'SELECT ' || sequence_names(item_index) || '.NEXTVAL FROM dual'
        INTO sequence_value;
    END IF;

    EXECUTE IMMEDIATE
      'ALTER SEQUENCE ' || sequence_names(item_index) ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';

    EXECUTE IMMEDIATE
      'ALTER TABLE ' || table_names(item_index) ||
      ' MODIFY (' || column_names(item_index) ||
      ' DEFAULT ' || sequence_names(item_index) || '.NEXTVAL)';
  END LOOP;
END;
/

COMMIT;

SELECT sequence_name, last_number, increment_by, cache_size
FROM user_sequences
WHERE sequence_name IN (
  'SEQ_ROLES',
  'SEQ_RECRUITMENTS',
  'SEQ_RECRUITMENT_APPLICATIONS',
  'SEQ_VENUES',
  'SEQ_VENUE_RESERVATIONS',
  'SEQ_PROJECTS',
  'SEQ_PROJECT_MEMBERS',
  'SEQ_PROJECT_TASKS',
  'SEQ_PROJECT_TASK_ASSIGNEES',
  'SEQ_PROJECT_TASK_PROGRESS_REPORTS',
  'SEQ_LEARNING_ITEMS',
  'SEQ_LEARNING_RECORDS',
  'SEQ_MATERIALS',
  'SEQ_MATERIAL_BORROWS',
  'SEQ_NOTICES',
  'SEQ_FORUM_POSTS',
  'SEQ_OPERATION_LOGS'
)
ORDER BY sequence_name;

SELECT table_name, column_name, data_default
FROM user_tab_columns
WHERE (table_name, column_name) IN (
  ('ROLES', 'ROLE_ID'),
  ('RECRUITMENTS', 'RECRUIT_ID'),
  ('RECRUITMENT_APPLICATIONS', 'APPLICATION_ID'),
  ('VENUES', 'VENUE_ID'),
  ('VENUE_RESERVATIONS', 'RESERVATION_ID'),
  ('PROJECTS', 'PROJECT_ID'),
  ('PROJECT_MEMBERS', 'PROJECT_MEMBER_ID'),
  ('PROJECT_TASKS', 'TASK_ID'),
  ('PROJECT_TASK_ASSIGNEES', 'TASK_ASSIGNEE_ID'),
  ('PROJECT_TASK_PROGRESS_REPORTS', 'TASK_PROGRESS_REPORT_ID'),
  ('LEARNING_ITEMS', 'ITEM_ID'),
  ('LEARNING_RECORDS', 'RECORD_ID'),
  ('MATERIALS', 'MATERIAL_ID'),
  ('MATERIAL_BORROWS', 'BORROW_ID'),
  ('NOTICES', 'NOTICE_ID'),
  ('FORUM_POSTS', 'POST_ID'),
  ('OPERATION_LOGS', 'LOG_ID')
)
ORDER BY table_name, column_name;
