-- Issue #78：把认证与社团核心实体主键迁移为 Oracle sequence 生成。
--
-- 执行前：
--   1. 备份目标 schema。
--   2. 确认下方重复数据预检可以通过；脚本不会自动删除业务数据。
--   3. 使用 CLUBHUB schema 所有者执行。
--
-- 脚本可重复执行。已存在的 sequence 会被推进到当前表最大主键之后，
-- 且不会低于 1000000，避免显式 seed ID 或历史数据与后续数据库生成 ID 冲突。

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  duplicate_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO duplicate_count
    FROM (
      SELECT username
        FROM users
       WHERE username IS NOT NULL
       GROUP BY username
      HAVING COUNT(*) > 1
    );
  IF duplicate_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20078, 'USERS.username 存在重复值，请先清理后再迁移。');
  END IF;

  SELECT COUNT(*)
    INTO duplicate_count
    FROM (
      SELECT student_no
        FROM users
       WHERE student_no IS NOT NULL
       GROUP BY student_no
      HAVING COUNT(*) > 1
    );
  IF duplicate_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20079, 'USERS.student_no 存在重复值，请先清理后再迁移。');
  END IF;

  SELECT COUNT(*)
    INTO duplicate_count
    FROM (
      SELECT user_id, role_id, NVL(club_id, -1) AS normalized_club_id
        FROM user_roles
       GROUP BY user_id, role_id, NVL(club_id, -1)
      HAVING COUNT(*) > 1
    );
  IF duplicate_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20080, 'USER_ROLES 存在重复角色作用域，请先清理后再迁移。');
  END IF;
END;
/

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(user_id), 0) INTO max_id FROM users;
  target_id := GREATEST(max_id + 1, 1000000);
  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_USERS START WITH ' || TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  EXECUTE IMMEDIATE 'SELECT SEQ_USERS.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_USERS INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_USERS.NEXTVAL FROM dual' INTO sequence_value;
  END IF;
  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_USERS INCREMENT BY 1';
END;
/

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(user_role_id), 0) INTO max_id FROM user_roles;
  target_id := GREATEST(max_id + 1, 1000000);
  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_USER_ROLES START WITH ' ||
      TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  EXECUTE IMMEDIATE 'SELECT SEQ_USER_ROLES.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_USER_ROLES INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_USER_ROLES.NEXTVAL FROM dual' INTO sequence_value;
  END IF;
  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_USER_ROLES INCREMENT BY 1';
END;
/

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(club_id), 0) INTO max_id FROM clubs;
  target_id := GREATEST(max_id + 1, 1000000);
  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_CLUBS START WITH ' || TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  EXECUTE IMMEDIATE 'SELECT SEQ_CLUBS.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_CLUBS INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_CLUBS.NEXTVAL FROM dual' INTO sequence_value;
  END IF;
  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_CLUBS INCREMENT BY 1';
END;
/

DECLARE
  max_id NUMBER;
  target_id NUMBER;
  sequence_value NUMBER;
BEGIN
  SELECT NVL(MAX(member_id), 0) INTO max_id FROM club_members;
  target_id := GREATEST(max_id + 1, 1000000);
  BEGIN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_CLUB_MEMBERS START WITH ' ||
      TO_CHAR(target_id, 'FM99999999999999999990') ||
      ' INCREMENT BY 1 NOCACHE NOCYCLE';
  EXCEPTION
    WHEN OTHERS THEN
      IF SQLCODE != -955 THEN
        RAISE;
      END IF;
  END;

  EXECUTE IMMEDIATE 'SELECT SEQ_CLUB_MEMBERS.NEXTVAL FROM dual' INTO sequence_value;
  IF sequence_value < target_id THEN
    EXECUTE IMMEDIATE
      'ALTER SEQUENCE SEQ_CLUB_MEMBERS INCREMENT BY ' ||
      TO_CHAR(target_id - sequence_value, 'FM99999999999999999990');
    EXECUTE IMMEDIATE 'SELECT SEQ_CLUB_MEMBERS.NEXTVAL FROM dual' INTO sequence_value;
  END IF;
  EXECUTE IMMEDIATE 'ALTER SEQUENCE SEQ_CLUB_MEMBERS INCREMENT BY 1';
END;
/

ALTER TABLE users MODIFY (user_id DEFAULT SEQ_USERS.NEXTVAL);
ALTER TABLE user_roles MODIFY (user_role_id DEFAULT SEQ_USER_ROLES.NEXTVAL);
ALTER TABLE clubs MODIFY (club_id DEFAULT SEQ_CLUBS.NEXTVAL);
ALTER TABLE club_members MODIFY (member_id DEFAULT SEQ_CLUB_MEMBERS.NEXTVAL);

BEGIN
  EXECUTE IMMEDIATE 'CREATE UNIQUE INDEX UQ_USERS_USERNAME ON USERS (username)';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -955 THEN
      RAISE;
    END IF;
END;
/

BEGIN
  EXECUTE IMMEDIATE 'CREATE UNIQUE INDEX UQ_USERS_STUDENT_NO ON USERS (student_no)';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -955 THEN
      RAISE;
    END IF;
END;
/

BEGIN
  EXECUTE IMMEDIATE
    'CREATE UNIQUE INDEX UQ_USER_ROLES_SCOPE ON USER_ROLES ' ||
    '(user_id, role_id, NVL(club_id, -1))';
EXCEPTION
  WHEN OTHERS THEN
    IF SQLCODE != -955 THEN
      RAISE;
    END IF;
END;
/

COMMIT;
