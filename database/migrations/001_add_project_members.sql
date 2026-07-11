WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET DEFINE OFF

-- 仅用于已有开发/测试 schema 的一次性迁移。
-- 执行前必须确认当前用户与目标 schema；生产和演示库禁止直接运行。
DECLARE
  project_members_count NUMBER;
  projects_count NUMBER;
  users_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO project_members_count
  FROM user_tables
  WHERE table_name = 'PROJECT_MEMBERS';

  IF project_members_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20001, 'PROJECT_MEMBERS already exists; stop and inspect the schema.');
  END IF;

  SELECT COUNT(*) INTO projects_count
  FROM user_tables
  WHERE table_name = 'PROJECTS';

  SELECT COUNT(*) INTO users_count
  FROM user_tables
  WHERE table_name = 'USERS';

  IF projects_count = 0 OR users_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20002, 'PROJECTS and USERS must exist before this migration.');
  END IF;
END;
/

CREATE TABLE PROJECT_MEMBERS (
  project_member_id number PRIMARY KEY,
  project_id number NOT NULL,
  user_id number NOT NULL,
  member_role varchar2(30) DEFAULT 'member' NOT NULL,
  member_status varchar2(30) DEFAULT 'active' NOT NULL,
  joined_at date DEFAULT SYSDATE NOT NULL,
  left_at date,
  remark varchar2(255),
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_PROJECT_MEMBERS_USER UNIQUE (project_id, user_id),
  CONSTRAINT CK_PROJECT_MEMBERS_ROLE CHECK (member_role IN ('leader', 'member', 'mentor')),
  CONSTRAINT CK_PROJECT_MEMBERS_STATUS CHECK (member_status IN ('active', 'removed', 'quit')),
  CONSTRAINT FK_PM_PROJECT FOREIGN KEY (project_id) REFERENCES PROJECTS (project_id) DEFERRABLE INITIALLY IMMEDIATE,
  CONSTRAINT FK_PM_USER FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE
);

INSERT INTO PROJECT_MEMBERS (
  project_member_id,
  project_id,
  user_id,
  member_role,
  member_status,
  joined_at,
  left_at,
  remark,
  created_at,
  updated_at
)
SELECT
  ROW_NUMBER() OVER (ORDER BY p.project_id),
  p.project_id,
  p.leader_user_id,
  'leader',
  'active',
  NVL(p.created_at, SYSDATE),
  NULL,
  NULL,
  NVL(p.created_at, SYSDATE),
  SYSDATE
FROM PROJECTS p
WHERE p.leader_user_id IS NOT NULL;

COMMIT;
