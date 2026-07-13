-- 业务代码使用数据库生成主键。序列从较高区间起步，兼容 seeds 中保留的显式演示 ID。
CREATE SEQUENCE SEQ_USERS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_USER_ROLES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_CLUBS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_CLUB_MEMBERS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_EVALUATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_ACTIVITIES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_ACTIVITY_PARTICIPATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;

CREATE TABLE USERS (
  user_id number DEFAULT SEQ_USERS.NEXTVAL PRIMARY KEY,
  username varchar2(255),
  password_hash varchar2(255),
  real_name varchar2(255),
  student_no varchar2(255),
  gender varchar2(255),
  phone varchar2(255),
  email varchar2(255),
  college varchar2(255),
  major varchar2(255),
  grade varchar2(255),
  account_status varchar2(255),
  created_at date,
  updated_at date
);

CREATE TABLE ROLES (
  role_id number PRIMARY KEY,
  role_code varchar2(255),
  role_name varchar2(255),
  role_scope varchar2(255),
  permission_desc clob,
  created_at date
);

CREATE TABLE USER_ROLES (
  user_role_id number DEFAULT SEQ_USER_ROLES.NEXTVAL PRIMARY KEY,
  user_id number,
  role_id number,
  club_id number,
  assigned_at date
);

CREATE TABLE CLUBS (
  club_id number DEFAULT SEQ_CLUBS.NEXTVAL PRIMARY KEY,
  club_name varchar2(255),
  category varchar2(255),
  description clob,
  logo_url varchar2(255),
  applicant_user_id number,
  president_user_id number,
  advisor_name varchar2(255),
  contact_phone varchar2(255),
  apply_reason clob,
  material_url varchar2(255),
  audit_status varchar2(255),
  reviewer_user_id number,
  review_comment varchar2(255),
  club_status varchar2(255),
  founded_at date,
  created_at date,
  updated_at date
);

CREATE TABLE CLUB_MEMBERS (
  member_id number DEFAULT SEQ_CLUB_MEMBERS.NEXTVAL PRIMARY KEY,
  club_id number,
  user_id number,
  department_name varchar2(255),
  group_name varchar2(255),
  position_name varchar2(255),
  term_name varchar2(255),
  term_start date,
  term_end date,
  member_status varchar2(255),
  join_at date,
  contribution_score number
);

CREATE TABLE RECRUITMENTS (
  recruit_id number PRIMARY KEY,
  club_id number,
  title varchar2(255),
  description clob,
  start_at date,
  end_at date,
  quota number,
  requirements clob,
  recruit_status varchar2(255),
  created_at date
);

CREATE TABLE RECRUITMENT_APPLICATIONS (
  application_id number PRIMARY KEY,
  recruit_id number,
  user_id number,
  application_reason clob,
  interview_score number,
  application_status varchar2(255),
  reviewer_user_id number,
  submitted_at date,
  reviewed_at date
);

CREATE TABLE ACTIVITIES (
  activity_id number DEFAULT SEQ_ACTIVITIES.NEXTVAL PRIMARY KEY,
  club_id number,
  creator_user_id number,
  title varchar2(255),
  activity_type varchar2(255),
  description clob,
  location varchar2(255),
  start_at date,
  end_at date,
  capacity number,
  registration_deadline date,
  checkin_code varchar2(255),
  checkin_start_at date,
  checkin_end_at date,
  checkout_code varchar2(255),
  checkout_start_at date,
  checkout_end_at date,
  activity_status varchar2(255),
  reviewer_user_id number,
  review_comment varchar2(255),
  budget_amount number,
  budget_purpose varchar2(255),
  budget_detail clob,
  budget_status varchar2(255),
  budget_reviewer_id number,
  budget_comment varchar2(255),
  published_at date,
  created_at date
);

CREATE TABLE ACTIVITY_PARTICIPATIONS (
  participation_id number DEFAULT SEQ_ACTIVITY_PARTICIPATIONS.NEXTVAL PRIMARY KEY,
  activity_id number,
  user_id number,
  register_status varchar2(255),
  registered_at date,
  checkin_at date,
  checkout_at date,
  sign_status varchar2(255),
  remark varchar2(255)
);

CREATE TABLE VENUES (
  venue_id number PRIMARY KEY,
  manager_user_id number,
  venue_name varchar2(255),
  building varchar2(255),
  room_no varchar2(255),
  capacity number,
  venue_status varchar2(255),
  created_at date
);

CREATE TABLE VENUE_RESERVATIONS (
  reservation_id number PRIMARY KEY,
  venue_id number,
  club_id number,
  activity_id number,
  applicant_user_id number,
  start_at date,
  end_at date,
  purpose varchar2(255),
  reservation_status varchar2(255),
  reviewer_user_id number,
  review_comment varchar2(255),
  created_at date
);

CREATE TABLE PROJECTS (
  project_id number PRIMARY KEY,
  club_id number,
  project_name varchar2(255),
  description clob,
  leader_user_id number,
  start_date date,
  end_date date,
  project_status varchar2(255),
  reviewer_user_id number,
  review_comment varchar2(255),
  created_at date
);

CREATE TABLE PROJECT_MEMBERS (
  project_member_id number PRIMARY KEY,
  project_id number NOT NULL,
  user_id number NOT NULL,
  member_role varchar2(30) DEFAULT 'member' NOT NULL,
  member_status varchar2(30) DEFAULT 'active' NOT NULL,
  joined_at date DEFAULT SYSDATE NOT NULL,
  left_at date,
  remark varchar2(255 char),
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_PROJECT_MEMBERS_USER UNIQUE (project_id, user_id),
  CONSTRAINT CK_PROJECT_MEMBERS_ROLE CHECK (member_role IN ('leader', 'member', 'mentor')),
  CONSTRAINT CK_PROJECT_MEMBERS_STATUS CHECK (member_status IN ('active', 'removed', 'quit'))
);

CREATE UNIQUE INDEX UQ_PM_ACTIVE_LEADER ON PROJECT_MEMBERS (
  CASE WHEN member_role = 'leader' AND member_status = 'active' THEN project_id END
);

CREATE TABLE PROJECT_TASKS (
  task_id number PRIMARY KEY,
  project_id number,
  assignee_user_id number,
  title varchar2(255),
  content clob,
  priority varchar2(255),
  start_date date,
  due_date date,
  finish_date date,
  progress number,
  task_status varchar2(255),
  delay_reason varchar2(255),
  deliverable_title varchar2(255),
  deliverable_desc clob,
  deliverable_url varchar2(255),
  deliverable_status varchar2(255),
  reviewer_user_id number,
  review_comment varchar2(255),
  deliverable_submitter_id number,
  deliverable_submitted_at date,
  deliverable_reviewed_at date
);

CREATE TABLE PROJECT_TASK_ASSIGNEES (
  task_assignee_id number PRIMARY KEY,
  task_id number NOT NULL,
  user_id number NOT NULL,
  assigned_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_PROJECT_TASK_ASSIGNEES UNIQUE (task_id, user_id)
);

CREATE INDEX IX_PROJECT_TASK_ASSIGNEES_USER ON PROJECT_TASK_ASSIGNEES (user_id, task_id);

CREATE TABLE PROJECT_TASK_PROGRESS_REPORTS (
  task_progress_report_id number PRIMARY KEY,
  task_id number NOT NULL,
  reporter_user_id number NOT NULL,
  progress number NOT NULL,
  task_status varchar2(30) NOT NULL,
  report_content varchar2(1000 char),
  delay_reason varchar2(255 char),
  submitted_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT CK_PT_PROGRESS_REPORTS_PROGRESS CHECK (progress BETWEEN 0 AND 100),
  CONSTRAINT CK_PT_PROGRESS_REPORTS_STATUS CHECK (task_status IN ('pending', 'in_progress', 'completed', 'delayed'))
);

CREATE INDEX IX_PT_PROGRESS_REPORTS_TASK ON PROJECT_TASK_PROGRESS_REPORTS (task_id, submitted_at);

CREATE TABLE LEARNING_ITEMS (
  item_id number PRIMARY KEY,
  club_id number,
  uploader_user_id number,
  teacher_user_id number,
  title varchar2(255),
  item_type varchar2(255),
  category_name varchar2(255),
  description clob,
  file_url varchar2(255),
  start_at date,
  end_at date,
  capacity number,
  visibility varchar2(255),
  download_permission varchar2(255),
  item_status varchar2(255),
  created_at date
);

CREATE TABLE LEARNING_RECORDS (
  record_id number PRIMARY KEY,
  item_id number,
  user_id number,
  enroll_status varchar2(255),
  enrolled_at date,
  progress number,
  duration_seconds number,
  last_learn_at date,
  completed_at date,
  downloaded_at date,
  download_ip varchar2(255)
);

CREATE TABLE MATERIALS (
  material_id number PRIMARY KEY,
  club_id number,
  material_name varchar2(255),
  specification varchar2(255),
  total_qty number,
  available_qty number,
  storage_location varchar2(255),
  material_status varchar2(255),
  created_at date
);

CREATE TABLE MATERIAL_BORROWS (
  borrow_id number PRIMARY KEY,
  material_id number,
  club_id number,
  borrower_user_id number,
  quantity number,
  borrow_at date,
  expected_return_at date,
  return_at date,
  borrow_status varchar2(255),
  damage_desc varchar2(255),
  compensation_amount number
);

CREATE TABLE EVALUATIONS (
  evaluation_id number DEFAULT SEQ_EVALUATIONS.NEXTVAL PRIMARY KEY,
  evaluation_type varchar2(255),
  club_id number,
  user_id number,
  evaluator_user_id number,
  term_name varchar2(255),
  award_title varchar2(255),
  award_level varchar2(255),
  award_reason varchar2(255),
  activity_score number,
  task_score number,
  learning_score number,
  award_score number,
  total_score number,
  grade varchar2(255),
  public_status varchar2(255),
  comment_text varchar2(255),
  created_at date
);

CREATE TABLE NOTICES (
  notice_id number PRIMARY KEY,
  club_id number,
  publisher_user_id number,
  notice_type varchar2(255),
  title varchar2(255),
  content clob,
  target_type varchar2(255),
  target_id number,
  publish_at date,
  expire_at date,
  notice_status varchar2(255)
);

CREATE TABLE NOTICE_READS (
  read_id number PRIMARY KEY,
  notice_id number,
  user_id number,
  read_at date
);

CREATE TABLE FORUM_POSTS (
  post_id number PRIMARY KEY,
  club_id number,
  user_id number,
  parent_post_id number,
  title varchar2(255),
  content clob,
  is_top number,
  post_status varchar2(255),
  created_at date,
  updated_at date
);

CREATE TABLE OPERATION_LOGS (
  log_id number PRIMARY KEY,
  user_id number,
  module_name varchar2(255),
  operation_type varchar2(255),
  target_table varchar2(255),
  target_id number,
  ip_address varchar2(255),
  created_at date
);

CREATE UNIQUE INDEX UQ_USERS_USERNAME ON USERS (username);

CREATE UNIQUE INDEX UQ_USERS_STUDENT_NO ON USERS (student_no);

-- NVL 让全局角色（club_id 为空）也只能分配一次。
CREATE UNIQUE INDEX UQ_USER_ROLES_SCOPE ON USER_ROLES (user_id, role_id, NVL(club_id, -1));

ALTER TABLE USER_ROLES ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE USER_ROLES ADD FOREIGN KEY (role_id) REFERENCES ROLES (role_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE USER_ROLES ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUBS ADD FOREIGN KEY (applicant_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUBS ADD FOREIGN KEY (president_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUBS ADD FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_MEMBERS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_MEMBERS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE RECRUITMENTS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE RECRUITMENT_APPLICATIONS ADD FOREIGN KEY (recruit_id) REFERENCES RECRUITMENTS (recruit_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE RECRUITMENT_APPLICATIONS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE RECRUITMENT_APPLICATIONS ADD FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE ACTIVITIES ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE ACTIVITIES ADD FOREIGN KEY (creator_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE ACTIVITIES ADD FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE ACTIVITIES ADD FOREIGN KEY (budget_reviewer_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE ACTIVITY_PARTICIPATIONS ADD FOREIGN KEY (activity_id) REFERENCES ACTIVITIES (activity_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE ACTIVITY_PARTICIPATIONS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE VENUES ADD FOREIGN KEY (manager_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE VENUE_RESERVATIONS ADD FOREIGN KEY (venue_id) REFERENCES VENUES (venue_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE VENUE_RESERVATIONS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE VENUE_RESERVATIONS ADD FOREIGN KEY (activity_id) REFERENCES ACTIVITIES (activity_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE VENUE_RESERVATIONS ADD FOREIGN KEY (applicant_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE VENUE_RESERVATIONS ADD FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECTS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECTS ADD FOREIGN KEY (leader_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECTS ADD FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_MEMBERS ADD CONSTRAINT FK_PM_PROJECT FOREIGN KEY (project_id) REFERENCES PROJECTS (project_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_MEMBERS ADD CONSTRAINT FK_PM_USER FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASKS ADD FOREIGN KEY (project_id) REFERENCES PROJECTS (project_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASKS ADD FOREIGN KEY (assignee_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASKS ADD FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASKS ADD FOREIGN KEY (deliverable_submitter_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASK_ASSIGNEES ADD FOREIGN KEY (task_id) REFERENCES PROJECT_TASKS (task_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASK_ASSIGNEES ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASK_PROGRESS_REPORTS ADD FOREIGN KEY (task_id) REFERENCES PROJECT_TASKS (task_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE PROJECT_TASK_PROGRESS_REPORTS ADD FOREIGN KEY (reporter_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE LEARNING_ITEMS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE LEARNING_ITEMS ADD FOREIGN KEY (uploader_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE LEARNING_ITEMS ADD FOREIGN KEY (teacher_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE LEARNING_RECORDS ADD FOREIGN KEY (item_id) REFERENCES LEARNING_ITEMS (item_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE LEARNING_RECORDS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE MATERIALS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE MATERIAL_BORROWS ADD FOREIGN KEY (material_id) REFERENCES MATERIALS (material_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE MATERIAL_BORROWS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE MATERIAL_BORROWS ADD FOREIGN KEY (borrower_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATIONS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATIONS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATIONS ADD FOREIGN KEY (evaluator_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICES ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICES ADD FOREIGN KEY (publisher_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICE_READS ADD FOREIGN KEY (notice_id) REFERENCES NOTICES (notice_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICE_READS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE FORUM_POSTS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE FORUM_POSTS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE FORUM_POSTS ADD FOREIGN KEY (parent_post_id) REFERENCES FORUM_POSTS (post_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE OPERATION_LOGS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;
