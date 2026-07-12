-- Issue #90：把活动与参与记录主键迁移为 Oracle sequence 生成。
--
-- 执行前：
--   1. 备份目标 schema。
--   2. 使用 CLUBHUB schema 所有者执行。
--
-- 影响范围：新增 SEQ_ACTIVITIES、SEQ_ACTIVITY_PARTICIPATIONS，并修改
-- ACTIVITIES.activity_id、ACTIVITY_PARTICIPATIONS.participation_id 的默认值；
-- 不更新或删除任何已有业务行。
--
-- 回滚方案：Oracle DDL 会隐式提交，WHENEVER SQLERROR ROLLBACK 无法撤销已经
-- 生效的 DDL。如需回退，请在停止活动写入后手工执行：
--   ALTER TABLE activities MODIFY (activity_id DEFAULT NULL);
--   ALTER TABLE activity_participations MODIFY (participation_id DEFAULT NULL);
--   DROP SEQUENCE seq_activities;
--   DROP SEQUENCE seq_activity_participations;
-- 随后再回退到仍由应用层生成主键的后端版本，或从执行前备份恢复。
--
-- 脚本可重复执行。sequence 会被推进到当前最大主键之后，
-- 且不会低于 1000000，避免显式 seed ID 与后续数据库生成 ID 冲突。

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(activity_id), 0) INTO max_id FROM activities;
  target_id := GREATEST(max_id + 1, 1000000);

  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_ACTIVITIES START WITH ' ||
      TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  EXECUTE IMMEDIATE 'SELECT SEQ_ACTIVITIES.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_ACTIVITIES INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_ACTIVITIES.NEXTVAL FROM dual' INTO sequence_value;
  END IF;

  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_ACTIVITIES INCREMENT BY 1';
END;
/

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(participation_id), 0) INTO max_id FROM activity_participations;
  target_id := GREATEST(max_id + 1, 1000000);

  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_ACTIVITY_PARTICIPATIONS START WITH ' ||
      TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  EXECUTE IMMEDIATE 'SELECT SEQ_ACTIVITY_PARTICIPATIONS.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_ACTIVITY_PARTICIPATIONS INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_ACTIVITY_PARTICIPATIONS.NEXTVAL FROM dual' INTO sequence_value;
  END IF;

  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_ACTIVITY_PARTICIPATIONS INCREMENT BY 1';
END;
/

ALTER TABLE activities MODIFY (activity_id DEFAULT SEQ_ACTIVITIES.NEXTVAL);
ALTER TABLE activity_participations MODIFY (participation_id DEFAULT SEQ_ACTIVITY_PARTICIPATIONS.NEXTVAL);

COMMIT;
