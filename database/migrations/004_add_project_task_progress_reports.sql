-- Issue #17 后续：记录每次项目任务进度提交，供任务历史可视化追溯。
-- 仅可在已确认的共享开发库或明确测试库执行，禁止用于生产/演示库。
-- Oracle DDL 会隐式提交；执行前请备份并确认 PROJECT_TASKS、USERS 已存在。

DECLARE
  table_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_count FROM user_tables WHERE table_name = 'PROJECT_TASK_PROGRESS_REPORTS';
  IF table_count > 0 THEN
    RAISE_APPLICATION_ERROR(-20031, 'PROJECT_TASK_PROGRESS_REPORTS already exists; stop and inspect the schema.');
  END IF;

  SELECT COUNT(*) INTO table_count FROM user_tables WHERE table_name = 'PROJECT_TASKS';
  IF table_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20032, 'PROJECT_TASKS does not exist; stop and inspect the schema.');
  END IF;

  SELECT COUNT(*) INTO table_count FROM user_tables WHERE table_name = 'USERS';
  IF table_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20033, 'USERS does not exist; stop and inspect the schema.');
  END IF;
END;
/

CREATE TABLE PROJECT_TASK_PROGRESS_REPORTS (
  task_progress_report_id NUMBER PRIMARY KEY,
  task_id NUMBER NOT NULL,
  reporter_user_id NUMBER NOT NULL,
  progress NUMBER NOT NULL,
  task_status VARCHAR2(30) NOT NULL,
  report_content VARCHAR2(1000 CHAR),
  delay_reason VARCHAR2(255 CHAR),
  submitted_at DATE DEFAULT SYSDATE NOT NULL,
  CONSTRAINT CK_PT_PROGRESS_REPORTS_PROGRESS CHECK (progress BETWEEN 0 AND 100),
  CONSTRAINT CK_PT_PROGRESS_REPORTS_STATUS CHECK (task_status IN ('pending', 'in_progress', 'completed', 'delayed')),
  CONSTRAINT FK_PTPR_TASK FOREIGN KEY (task_id) REFERENCES PROJECT_TASKS (task_id) DEFERRABLE INITIALLY IMMEDIATE,
  CONSTRAINT FK_PTPR_REPORTER FOREIGN KEY (reporter_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE
);

CREATE INDEX IX_PT_PROGRESS_REPORTS_TASK ON PROJECT_TASK_PROGRESS_REPORTS (task_id, submitted_at);

-- 既有任务没有可靠的逐次提交内容，故不回填或伪造历史记录。
COMMIT;
