WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET DEFINE OFF

-- 已有共享开发/测试库的增量迁移；禁止用于生产或演示库。
DECLARE
  table_count NUMBER;
  duplicate_leader_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_count FROM user_tables WHERE table_name = 'PROJECT_MEMBERS';
  IF table_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20011, 'PROJECT_MEMBERS does not exist; run 001_add_project_members.sql first.');
  END IF;

  SELECT COUNT(*) INTO duplicate_leader_count
  FROM (
    SELECT project_id
    FROM project_members
    WHERE member_role = 'leader' AND member_status = 'active'
    GROUP BY project_id
    HAVING COUNT(*) > 1
  );
  IF duplicate_leader_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20012, 'Multiple active leaders exist; reconcile data before creating UQ_PM_ACTIVE_LEADER.');
  END IF;
END;
/

ALTER TABLE PROJECT_MEMBERS MODIFY (remark VARCHAR2(255 CHAR));

DECLARE
  index_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_count FROM user_indexes WHERE index_name = 'UQ_PM_ACTIVE_LEADER';
  IF index_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE UNIQUE INDEX UQ_PM_ACTIVE_LEADER ON PROJECT_MEMBERS (
        CASE WHEN member_role = 'leader' AND member_status = 'active' THEN project_id END
      )]';
  END IF;
END;
/

COMMIT;
