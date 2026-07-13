-- Issue #17 后续：支持一项项目任务分配给多名项目成员。
-- 仅可在已确认的共享开发库或明确测试库执行，禁止用于生产/演示库。
-- Oracle DDL 会隐式提交；执行前请备份并确认 PROJECT_TASKS、USERS 已存在。
-- 影响范围：新增 PROJECT_TASK_ASSIGNEES 表并回填既有单人任务数据，不修改 PROJECT_TASKS 现有列。
-- 回滚方案：确认应用已回退到读取 PROJECT_TASKS.assignee_user_id 的旧逻辑后，手动执行
-- DROP TABLE PROJECT_TASK_ASSIGNEES CASCADE CONSTRAINTS; Oracle DDL 不能事务回滚，执行前请确认影响并保留备份。

DECLARE
  table_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_count FROM user_tables WHERE table_name = 'PROJECT_TASK_ASSIGNEES';
  IF table_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20021, 'PROJECT_TASK_ASSIGNEES already exists; stop and inspect the schema.');
  END IF;

  SELECT COUNT(*) INTO table_count FROM user_tables WHERE table_name = 'PROJECT_TASKS';
  IF table_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20022, 'PROJECT_TASKS does not exist; stop and inspect the schema.');
  END IF;
END;
/

CREATE TABLE PROJECT_TASK_ASSIGNEES (
  task_assignee_id NUMBER PRIMARY KEY,
  task_id NUMBER NOT NULL,
  user_id NUMBER NOT NULL,
  assigned_at DATE DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_PROJECT_TASK_ASSIGNEES UNIQUE (task_id, user_id),
  CONSTRAINT FK_PTA_TASK FOREIGN KEY (task_id) REFERENCES PROJECT_TASKS (task_id) DEFERRABLE INITIALLY IMMEDIATE,
  CONSTRAINT FK_PTA_USER FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE
);

CREATE INDEX IX_PROJECT_TASK_ASSIGNEES_USER ON PROJECT_TASK_ASSIGNEES (user_id, task_id);

-- 回填既有单人任务，保留历史 assignee_user_id 兼容字段。
INSERT INTO PROJECT_TASK_ASSIGNEES (task_assignee_id, task_id, user_id, assigned_at)
SELECT ROW_NUMBER() OVER (ORDER BY task_id), task_id, assignee_user_id, NVL(start_date, SYSDATE)
FROM PROJECT_TASKS
WHERE assignee_user_id IS NOT NULL;

COMMIT;
