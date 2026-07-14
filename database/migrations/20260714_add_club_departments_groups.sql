-- Issue #136: entity tables for club departments and groups.
-- Execute only after backing up an existing development or test database.
-- This migration keeps legacy text columns for compatibility, adds ID columns,
-- backfills IDs from existing text, and adds composite foreign keys to prevent
-- cross-club or cross-department references.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  invalid_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO invalid_count
  FROM club_members
  WHERE group_name IS NOT NULL
    AND TRIM(group_name) IS NOT NULL
    AND (department_name IS NULL OR TRIM(department_name) IS NULL);

  IF invalid_count > 0 THEN
    RAISE_APPLICATION_ERROR(
      -20061,
      'CLUB_MEMBERS has group_name without department_name; clean those rows before migrating.');
  END IF;
END;
/

DECLARE
  sequence_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO sequence_count
  FROM user_sequences
  WHERE sequence_name = 'SEQ_CLUB_DEPARTMENTS';

  IF sequence_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE SEQUENCE SEQ_CLUB_DEPARTMENTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE';
  END IF;

  SELECT COUNT(*) INTO sequence_count
  FROM user_sequences
  WHERE sequence_name = 'SEQ_CLUB_GROUPS';

  IF sequence_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE SEQUENCE SEQ_CLUB_GROUPS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE';
  END IF;
END;
/

DECLARE
  table_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_count
  FROM user_tables
  WHERE table_name = 'CLUB_DEPARTMENTS';

  IF table_count = 0 THEN
    EXECUTE IMMEDIATE '
      CREATE TABLE CLUB_DEPARTMENTS (
        department_id NUMBER DEFAULT SEQ_CLUB_DEPARTMENTS.NEXTVAL PRIMARY KEY,
        club_id NUMBER NOT NULL,
        department_name VARCHAR2(255 CHAR) NOT NULL,
        department_code VARCHAR2(100 CHAR),
        description CLOB,
        responsibilities CLOB,
        contact_phone VARCHAR2(255 CHAR),
        contact_email VARCHAR2(255 CHAR),
        office_location VARCHAR2(255 CHAR),
        display_order NUMBER DEFAULT 0 NOT NULL,
        department_status VARCHAR2(30 CHAR) DEFAULT ''active'' NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_CLUB_DEPARTMENTS_SCOPE UNIQUE (club_id, department_id),
        CONSTRAINT UQ_CLUB_DEPARTMENTS_NAME UNIQUE (club_id, department_name),
        CONSTRAINT CK_CLUB_DEPARTMENTS_STATUS CHECK (department_status IN (''active'', ''inactive''))
      )';
  END IF;

  SELECT COUNT(*) INTO table_count
  FROM user_tables
  WHERE table_name = 'CLUB_GROUPS';

  IF table_count = 0 THEN
    EXECUTE IMMEDIATE '
      CREATE TABLE CLUB_GROUPS (
        group_id NUMBER DEFAULT SEQ_CLUB_GROUPS.NEXTVAL PRIMARY KEY,
        club_id NUMBER NOT NULL,
        department_id NUMBER NOT NULL,
        group_name VARCHAR2(255 CHAR) NOT NULL,
        group_code VARCHAR2(100 CHAR),
        description CLOB,
        responsibilities CLOB,
        contact_phone VARCHAR2(255 CHAR),
        contact_email VARCHAR2(255 CHAR),
        activity_location VARCHAR2(255 CHAR),
        display_order NUMBER DEFAULT 0 NOT NULL,
        group_status VARCHAR2(30 CHAR) DEFAULT ''active'' NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_CLUB_GROUPS_SCOPE UNIQUE (club_id, group_id),
        CONSTRAINT UQ_CLUB_GROUPS_DEPT_SCOPE UNIQUE (club_id, department_id, group_id),
        CONSTRAINT UQ_CLUB_GROUPS_NAME UNIQUE (club_id, department_id, group_name),
        CONSTRAINT CK_CLUB_GROUPS_STATUS CHECK (group_status IN (''active'', ''inactive''))
      )';
  END IF;
END;
/

DECLARE
  column_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_count
  FROM user_tab_columns
  WHERE table_name = 'CLUB_MEMBERS'
    AND column_name = 'DEPARTMENT_ID';

  IF column_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_MEMBERS ADD (department_id NUMBER)';
  END IF;

  SELECT COUNT(*) INTO column_count
  FROM user_tab_columns
  WHERE table_name = 'CLUB_MEMBERS'
    AND column_name = 'GROUP_ID';

  IF column_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_MEMBERS ADD (group_id NUMBER)';
  END IF;
END;
/

INSERT INTO club_departments (
  department_id,
  club_id,
  department_name,
  display_order,
  department_status,
  created_at,
  updated_at
)
SELECT
  SEQ_CLUB_DEPARTMENTS.NEXTVAL,
  source.club_id,
  source.department_name,
  ROW_NUMBER() OVER (PARTITION BY source.club_id ORDER BY source.department_name),
  'active',
  SYSDATE,
  SYSDATE
FROM (
  SELECT DISTINCT club_id, TRIM(department_name) AS department_name
  FROM club_members
  WHERE club_id IS NOT NULL
    AND department_name IS NOT NULL
    AND TRIM(department_name) IS NOT NULL
) source
WHERE NOT EXISTS (
  SELECT 1
  FROM club_departments existing
  WHERE existing.club_id = source.club_id
    AND existing.department_name = source.department_name
);

UPDATE club_members member
SET department_id = (
  SELECT department.department_id
  FROM club_departments department
  WHERE department.club_id = member.club_id
    AND department.department_name = TRIM(member.department_name)
)
WHERE member.department_id IS NULL
  AND member.club_id IS NOT NULL
  AND member.department_name IS NOT NULL
  AND TRIM(member.department_name) IS NOT NULL;

INSERT INTO club_groups (
  group_id,
  club_id,
  department_id,
  group_name,
  display_order,
  group_status,
  created_at,
  updated_at
)
SELECT
  SEQ_CLUB_GROUPS.NEXTVAL,
  source.club_id,
  source.department_id,
  source.group_name,
  ROW_NUMBER() OVER (PARTITION BY source.club_id, source.department_id ORDER BY source.group_name),
  'active',
  SYSDATE,
  SYSDATE
FROM (
  SELECT DISTINCT
    member.club_id,
    member.department_id,
    TRIM(member.group_name) AS group_name
  FROM club_members member
  WHERE member.club_id IS NOT NULL
    AND member.department_id IS NOT NULL
    AND member.group_name IS NOT NULL
    AND TRIM(member.group_name) IS NOT NULL
) source
WHERE NOT EXISTS (
  SELECT 1
  FROM club_groups existing
  WHERE existing.club_id = source.club_id
    AND existing.department_id = source.department_id
    AND existing.group_name = source.group_name
);

UPDATE club_members member
SET group_id = (
  SELECT club_group.group_id
  FROM club_groups club_group
  WHERE club_group.club_id = member.club_id
    AND club_group.department_id = member.department_id
    AND club_group.group_name = TRIM(member.group_name)
)
WHERE member.group_id IS NULL
  AND member.club_id IS NOT NULL
  AND member.department_id IS NOT NULL
  AND member.group_name IS NOT NULL
  AND TRIM(member.group_name) IS NOT NULL;

DECLARE
  missing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO missing_count
  FROM club_members
  WHERE department_name IS NOT NULL
    AND TRIM(department_name) IS NOT NULL
    AND department_id IS NULL;

  IF missing_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20062, 'Some CLUB_MEMBERS.department_name values were not backfilled.');
  END IF;

  SELECT COUNT(*) INTO missing_count
  FROM club_members
  WHERE group_name IS NOT NULL
    AND TRIM(group_name) IS NOT NULL
    AND group_id IS NULL;

  IF missing_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20063, 'Some CLUB_MEMBERS.group_name values were not backfilled.');
  END IF;
END;
/

DECLARE
  constraint_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO constraint_count
  FROM user_constraints
  WHERE constraint_name = 'FK_CLUB_DEPARTMENTS_CLUB';

  IF constraint_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_DEPARTMENTS ADD CONSTRAINT FK_CLUB_DEPARTMENTS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE';
  END IF;

  SELECT COUNT(*) INTO constraint_count
  FROM user_constraints
  WHERE constraint_name = 'FK_CLUB_GROUPS_DEPARTMENT';

  IF constraint_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_GROUPS ADD CONSTRAINT FK_CLUB_GROUPS_DEPARTMENT FOREIGN KEY (club_id, department_id) REFERENCES CLUB_DEPARTMENTS (club_id, department_id) DEFERRABLE INITIALLY IMMEDIATE';
  END IF;

  SELECT COUNT(*) INTO constraint_count
  FROM user_constraints
  WHERE constraint_name = 'CK_CLUB_MEMBERS_GROUP_DEPT';

  IF constraint_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_MEMBERS ADD CONSTRAINT CK_CLUB_MEMBERS_GROUP_DEPT CHECK (group_id IS NULL OR department_id IS NOT NULL)';
  END IF;

  SELECT COUNT(*) INTO constraint_count
  FROM user_constraints
  WHERE constraint_name = 'FK_CLUB_MEMBERS_DEPARTMENT';

  IF constraint_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_MEMBERS ADD CONSTRAINT FK_CLUB_MEMBERS_DEPARTMENT FOREIGN KEY (club_id, department_id) REFERENCES CLUB_DEPARTMENTS (club_id, department_id) DEFERRABLE INITIALLY IMMEDIATE';
  END IF;

  SELECT COUNT(*) INTO constraint_count
  FROM user_constraints
  WHERE constraint_name = 'FK_CLUB_MEMBERS_GROUP';

  IF constraint_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE CLUB_MEMBERS ADD CONSTRAINT FK_CLUB_MEMBERS_GROUP FOREIGN KEY (club_id, department_id, group_id) REFERENCES CLUB_GROUPS (club_id, department_id, group_id) DEFERRABLE INITIALLY IMMEDIATE';
  END IF;
END;
/

DECLARE
  index_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_count
  FROM user_indexes
  WHERE index_name = 'IX_CLUB_DEPARTMENTS_ORDER';

  IF index_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IX_CLUB_DEPARTMENTS_ORDER ON CLUB_DEPARTMENTS (club_id, display_order, department_name)';
  END IF;

  SELECT COUNT(*) INTO index_count
  FROM user_indexes
  WHERE index_name = 'IX_CLUB_GROUPS_ORDER';

  IF index_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IX_CLUB_GROUPS_ORDER ON CLUB_GROUPS (club_id, department_id, display_order, group_name)';
  END IF;

  SELECT COUNT(*) INTO index_count
  FROM user_indexes
  WHERE index_name = 'IX_CLUB_MEMBERS_ORG';

  IF index_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IX_CLUB_MEMBERS_ORG ON CLUB_MEMBERS (club_id, department_id, group_id)';
  END IF;
END;
/

COMMIT;
