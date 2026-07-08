-- 为社团纳新补充创建人，用于草稿可见性、草稿删除和“本社团提出的纳新”分组。
-- 适用于已有开发库或测试库；新库请直接使用 database/schema.sql。

DECLARE
  v_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO v_count
    FROM user_tab_columns
   WHERE table_name = 'RECRUITMENTS'
     AND column_name = 'CREATOR_USER_ID';

  IF v_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE RECRUITMENTS ADD (creator_user_id NUMBER)';
  END IF;
END;
/

UPDATE RECRUITMENTS r
   SET creator_user_id = (
       SELECT MIN(ur.user_id)
         FROM USER_ROLES ur
         JOIN ROLES ro ON ro.role_id = ur.role_id
        WHERE ur.club_id = r.club_id
          AND LOWER(ro.role_code) IN (
              'club_officer',
              'club_leader',
              'club_president',
              'club_manager',
              'president'
          )
   )
 WHERE r.creator_user_id IS NULL;

DECLARE
  v_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO v_count
    FROM user_constraints
   WHERE table_name = 'RECRUITMENTS'
     AND constraint_name = 'FK_RECRUITMENTS_CREATOR_USER';

  IF v_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE RECRUITMENTS ADD CONSTRAINT fk_recruitments_creator_user FOREIGN KEY (creator_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE';
  END IF;
END;
/

COMMIT;
