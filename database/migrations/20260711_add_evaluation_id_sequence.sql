-- Issue #114：把评价记录主键迁移为 Oracle sequence 生成。
--
-- 执行前：
--   1. 备份目标 schema。
--   2. 使用 CLUBHUB schema 所有者执行。
--
-- 脚本可重复执行。sequence 会被推进到当前最大 evaluation_id 之后，
-- 且不会低于 1000000，避免显式历史 ID 与后续数据库生成 ID 冲突。

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(evaluation_id), 0) INTO max_id FROM evaluations;
  target_id := GREATEST(max_id + 1, 1000000);

  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_EVALUATIONS START WITH ' ||
      TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  -- 使用动态 SQL，确保 sequence 首次创建前匿名块也能正常编译。
  EXECUTE IMMEDIATE 'SELECT SEQ_EVALUATIONS.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_EVALUATIONS INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_EVALUATIONS.NEXTVAL FROM dual' INTO sequence_value;
  END IF;

  -- 中断重跑时也恢复正常步长。
  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_EVALUATIONS INCREMENT BY 1';
END;
/

ALTER TABLE evaluations MODIFY (evaluation_id DEFAULT SEQ_EVALUATIONS.NEXTVAL);

COMMIT;
