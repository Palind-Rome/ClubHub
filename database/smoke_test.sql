-- ClubHub 数据库连通性冒烟测试。
-- 使用方式：以 CLUBHUB 应用用户连接 Oracle 后执行本脚本。
-- 本脚本会插入一条临时角色数据，然后立即回滚，不应留下测试数据。

SET SERVEROUTPUT ON;
SET VERIFY OFF;

SELECT USER AS current_user FROM dual;

SELECT COUNT(*) AS table_count
FROM user_tables;

DECLARE
  v_before_count NUMBER;
  v_after_insert_count NUMBER;
  v_after_rollback_count NUMBER;
BEGIN
  SELECT COUNT(*)
  INTO v_before_count
  FROM roles
  WHERE role_code = 'CLUBHUB_SMOKE_TEST';

  SAVEPOINT clubhub_smoke_test;

  INSERT INTO roles (
    role_id,
    role_code,
    role_name,
    role_scope,
    permission_desc,
    created_at
  )
  SELECT
    NVL(MIN(role_id), 0) - 1,
    'CLUBHUB_SMOKE_TEST',
    '数据库连通性测试角色',
    'SYSTEM',
    '临时插入测试，脚本结束前回滚。',
    SYSDATE
  FROM roles;

  SELECT COUNT(*)
  INTO v_after_insert_count
  FROM roles
  WHERE role_code = 'CLUBHUB_SMOKE_TEST';

  IF v_after_insert_count <> v_before_count + 1 THEN
    RAISE_APPLICATION_ERROR(-20001, '数据库插入测试失败。');
  END IF;

  ROLLBACK TO clubhub_smoke_test;

  SELECT COUNT(*)
  INTO v_after_rollback_count
  FROM roles
  WHERE role_code = 'CLUBHUB_SMOKE_TEST';

  IF v_after_rollback_count <> v_before_count THEN
    RAISE_APPLICATION_ERROR(-20002, '数据库回滚测试失败。');
  END IF;

  DBMS_OUTPUT.PUT_LINE('数据库 SELECT / INSERT / ROLLBACK 冒烟测试通过。');
END;
/

ROLLBACK;
