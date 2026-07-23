-- 业务代码使用数据库生成主键。序列从较高区间起步，兼容 seeds 中保留的显式演示 ID。
CREATE SEQUENCE SEQ_USERS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_USER_ROLES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_CLUBS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_CLUB_MEMBERS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_CLUB_DEPARTMENTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_CLUB_GROUPS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_SCHEMES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_LEVELS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_APPLICATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_REVIEW_RECORDS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_ATTACHMENTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_PUBLICITY_BATCHES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_PUBLICITY_ITEMS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_AWARD_RULE_DOCUMENTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_EVALUATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_ACTIVITIES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_ACTIVITY_PARTICIPATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_NOTICE_READS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_ROLES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_RECRUITMENTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_RECRUITMENT_APPLICATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_VENUES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_VENUE_RESERVATIONS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_PROJECTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_PROJECT_MEMBERS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_PROJECT_TASKS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_PROJECT_TASK_ASSIGNEES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_PROJECT_TASK_PROGRESS_REPORTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_LEARNING_ITEMS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_LEARNING_RECORDS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_MATERIALS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_MATERIAL_BORROWS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_NOTICES START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_FORUM_POSTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;
CREATE SEQUENCE SEQ_OPERATION_LOGS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE;

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
  role_id number DEFAULT SEQ_ROLES.NEXTVAL PRIMARY KEY,
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

CREATE TABLE CLUB_DEPARTMENTS (
  department_id number DEFAULT SEQ_CLUB_DEPARTMENTS.NEXTVAL PRIMARY KEY,
  club_id number NOT NULL,
  department_name varchar2(255 char) NOT NULL,
  department_code varchar2(100 char),
  description clob,
  responsibilities clob,
  contact_phone varchar2(255 char),
  contact_email varchar2(255 char),
  office_location varchar2(255 char),
  display_order number DEFAULT 0 NOT NULL,
  department_status varchar2(30 char) DEFAULT 'active' NOT NULL,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_CLUB_DEPARTMENTS_SCOPE UNIQUE (club_id, department_id),
  CONSTRAINT UQ_CLUB_DEPARTMENTS_NAME UNIQUE (club_id, department_name),
  CONSTRAINT CK_CLUB_DEPARTMENTS_STATUS CHECK (department_status IN ('active', 'inactive'))
);

CREATE TABLE CLUB_GROUPS (
  group_id number DEFAULT SEQ_CLUB_GROUPS.NEXTVAL PRIMARY KEY,
  club_id number NOT NULL,
  department_id number NOT NULL,
  group_name varchar2(255 char) NOT NULL,
  group_code varchar2(100 char),
  description clob,
  responsibilities clob,
  contact_phone varchar2(255 char),
  contact_email varchar2(255 char),
  activity_location varchar2(255 char),
  display_order number DEFAULT 0 NOT NULL,
  group_status varchar2(30 char) DEFAULT 'active' NOT NULL,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_CLUB_GROUPS_SCOPE UNIQUE (club_id, group_id),
  CONSTRAINT UQ_CLUB_GROUPS_DEPT_SCOPE UNIQUE (club_id, department_id, group_id),
  CONSTRAINT UQ_CLUB_GROUPS_NAME UNIQUE (club_id, department_id, group_name),
  CONSTRAINT CK_CLUB_GROUPS_STATUS CHECK (group_status IN ('active', 'inactive'))
);

CREATE INDEX IX_CLUB_DEPARTMENTS_ORDER ON CLUB_DEPARTMENTS (club_id, display_order, department_name);

CREATE INDEX IX_CLUB_GROUPS_ORDER ON CLUB_GROUPS (club_id, department_id, display_order, group_name);

CREATE TABLE CLUB_MEMBERS (
  member_id number DEFAULT SEQ_CLUB_MEMBERS.NEXTVAL PRIMARY KEY,
  club_id number,
  user_id number,
  department_id number,
  group_id number,
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

CREATE INDEX IX_CLUB_MEMBERS_ORG ON CLUB_MEMBERS (club_id, department_id, group_id);

CREATE TABLE RECRUITMENTS (
  recruit_id number DEFAULT SEQ_RECRUITMENTS.NEXTVAL PRIMARY KEY,
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
  application_id number DEFAULT SEQ_RECRUITMENT_APPLICATIONS.NEXTVAL PRIMARY KEY,
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
  venue_id number DEFAULT SEQ_VENUES.NEXTVAL PRIMARY KEY,
  manager_user_id number,
  venue_name varchar2(255),
  building varchar2(255),
  room_no varchar2(255),
  capacity number,
  venue_status varchar2(255),
  created_at date
);

CREATE TABLE VENUE_RESERVATIONS (
  reservation_id number DEFAULT SEQ_VENUE_RESERVATIONS.NEXTVAL PRIMARY KEY,
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
  project_id number DEFAULT SEQ_PROJECTS.NEXTVAL PRIMARY KEY,
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
  project_member_id number DEFAULT SEQ_PROJECT_MEMBERS.NEXTVAL PRIMARY KEY,
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
  task_id number DEFAULT SEQ_PROJECT_TASKS.NEXTVAL PRIMARY KEY,
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
  task_assignee_id number DEFAULT SEQ_PROJECT_TASK_ASSIGNEES.NEXTVAL PRIMARY KEY,
  task_id number NOT NULL,
  user_id number NOT NULL,
  assigned_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_PROJECT_TASK_ASSIGNEES UNIQUE (task_id, user_id)
);

CREATE INDEX IX_PROJECT_TASK_ASSIGNEES_USER ON PROJECT_TASK_ASSIGNEES (user_id, task_id);

CREATE TABLE PROJECT_TASK_PROGRESS_REPORTS (
  task_progress_report_id number DEFAULT SEQ_PROJECT_TASK_PROGRESS_REPORTS.NEXTVAL PRIMARY KEY,
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
  item_id number DEFAULT SEQ_LEARNING_ITEMS.NEXTVAL PRIMARY KEY,
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
  record_id number DEFAULT SEQ_LEARNING_RECORDS.NEXTVAL PRIMARY KEY,
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
  material_id number DEFAULT SEQ_MATERIALS.NEXTVAL PRIMARY KEY,
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
  borrow_id number DEFAULT SEQ_MATERIAL_BORROWS.NEXTVAL PRIMARY KEY,
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

CREATE TABLE AWARD_SCHEMES (
  award_scheme_id number DEFAULT SEQ_AWARD_SCHEMES.NEXTVAL PRIMARY KEY,
  club_id number NOT NULL,
  award_name varchar2(255 char) NOT NULL,
  award_category varchar2(100 char) DEFAULT 'honor' NOT NULL,
  academic_year varchar2(50 char) NOT NULL,
  term_name varchar2(80 char),
  sponsor_unit varchar2(255 char),
  reward_level varchar2(100 char),
  funding_source varchar2(255 char),
  is_ranked number(1) DEFAULT 1 NOT NULL,
  is_fixed_amount number(1) DEFAULT 1 NOT NULL,
  description clob,
  material_description clob,
  application_start_at date,
  application_end_at date,
  publicity_start_at date,
  publicity_end_at date,
  scheme_status varchar2(30 char) DEFAULT 'draft' NOT NULL,
  created_by_user_id number,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_AWARD_SCHEMES_SCOPE UNIQUE (club_id, award_scheme_id),
  CONSTRAINT CK_AWARD_SCHEMES_CATEGORY CHECK (award_category IN ('honor', 'scholarship', 'competition', 'service', 'other')),
  CONSTRAINT CK_AWARD_SCHEMES_RANKED CHECK (is_ranked IN (0, 1)),
  CONSTRAINT CK_AWARD_SCHEMES_FIXED_AMOUNT CHECK (is_fixed_amount IN (0, 1)),
  CONSTRAINT CK_AWARD_SCHEMES_STATUS CHECK (scheme_status IN ('draft', 'open', 'reviewing', 'publicizing', 'archived', 'closed'))
);

CREATE UNIQUE INDEX UQ_AWARD_SCHEMES_NAME ON AWARD_SCHEMES (
  club_id,
  award_name,
  academic_year,
  NVL(term_name, '-')
);

CREATE INDEX IX_AWARD_SCHEMES_CLUB_STATUS ON AWARD_SCHEMES (club_id, scheme_status, application_start_at);

CREATE TABLE AWARD_LEVELS (
  award_level_id number DEFAULT SEQ_AWARD_LEVELS.NEXTVAL PRIMARY KEY,
  award_scheme_id number NOT NULL,
  level_name varchar2(255 char) NOT NULL,
  award_score number DEFAULT 0 NOT NULL,
  amount number,
  quota number,
  display_order number DEFAULT 0 NOT NULL,
  level_status varchar2(30 char) DEFAULT 'active' NOT NULL,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_AWARD_LEVELS_SCOPE UNIQUE (award_scheme_id, award_level_id),
  CONSTRAINT UQ_AWARD_LEVELS_NAME UNIQUE (award_scheme_id, level_name),
  CONSTRAINT CK_AWARD_LEVELS_SCORE CHECK (award_score BETWEEN 0 AND 100),
  CONSTRAINT CK_AWARD_LEVELS_AMOUNT CHECK (amount IS NULL OR amount >= 0),
  CONSTRAINT CK_AWARD_LEVELS_QUOTA CHECK (quota IS NULL OR quota >= 0),
  CONSTRAINT CK_AWARD_LEVELS_STATUS CHECK (level_status IN ('active', 'inactive'))
);

CREATE INDEX IX_AWARD_LEVELS_ORDER ON AWARD_LEVELS (award_scheme_id, display_order, level_name);

CREATE TABLE AWARD_APPLICATIONS (
  award_application_id number DEFAULT SEQ_AWARD_APPLICATIONS.NEXTVAL PRIMARY KEY,
  club_id number NOT NULL,
  award_scheme_id number NOT NULL,
  award_level_id number NOT NULL,
  applicant_user_id number NOT NULL,
  recommender_user_id number,
  submitter_user_id number NOT NULL,
  application_type varchar2(30 char) DEFAULT 'self' NOT NULL,
  application_reason clob,
  material_url varchar2(1000 char),
  current_step varchar2(30 char) DEFAULT 'student_submit' NOT NULL,
  application_status varchar2(30 char) DEFAULT 'draft' NOT NULL,
  public_status varchar2(30 char) DEFAULT 'none' NOT NULL,
  review_round number DEFAULT 1 NOT NULL,
  final_award_score number,
  final_amount number,
  submitted_at date,
  approved_at date,
  publicized_at date,
  archived_at date,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_AWARD_APPLICATIONS_SCOPE UNIQUE (club_id, award_application_id),
  CONSTRAINT UQ_AWARD_APPLICATIONS_MEMBER_SCOPE UNIQUE (club_id, applicant_user_id, award_application_id),
  CONSTRAINT UQ_AWARD_APPLICATIONS_APPLICANT UNIQUE (award_scheme_id, applicant_user_id),
  CONSTRAINT CK_AWARD_APPLICATIONS_TYPE CHECK (application_type IN ('self', 'recommendation')),
  CONSTRAINT CK_AWARD_APPLICATIONS_STEP CHECK (current_step IN ('student_submit', 'club_review', 'advisor_review', 'school_review', 'publicity', 'archived')),
  CONSTRAINT CK_AWARD_APPLICATIONS_STATUS CHECK (application_status IN ('draft', 'submitted', 'club_review', 'advisor_review', 'school_review', 'returned', 'rejected', 'approved', 'publicizing', 'publicized', 'archived', 'withdrawn')),
  CONSTRAINT CK_AWARD_APPLICATIONS_PUBLIC CHECK (public_status IN ('none', 'publicizing', 'publicized', 'withdrawn')),
  CONSTRAINT CK_AWARD_APPLICATIONS_ROUND CHECK (review_round >= 1),
  CONSTRAINT CK_AWARD_APPLICATIONS_SCORE CHECK (final_award_score IS NULL OR final_award_score BETWEEN 0 AND 100),
  CONSTRAINT CK_AWARD_APPLICATIONS_AMOUNT CHECK (final_amount IS NULL OR final_amount >= 0)
);

CREATE INDEX IX_AWARD_APPLICATIONS_STATUS ON AWARD_APPLICATIONS (club_id, application_status, current_step);

CREATE INDEX IX_AWARD_APPLICATIONS_USER ON AWARD_APPLICATIONS (applicant_user_id, club_id, award_scheme_id);

CREATE TABLE AWARD_REVIEW_RECORDS (
  review_id number DEFAULT SEQ_AWARD_REVIEW_RECORDS.NEXTVAL PRIMARY KEY,
  award_application_id number NOT NULL,
  review_round number DEFAULT 1 NOT NULL,
  review_step varchar2(30 char) NOT NULL,
  review_result varchar2(30 char) NOT NULL,
  reviewer_user_id number,
  review_comment clob,
  from_status varchar2(30 char),
  to_status varchar2(30 char),
  reviewed_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT CK_AWARD_REVIEWS_ROUND CHECK (review_round >= 1),
  CONSTRAINT CK_AWARD_REVIEWS_STEP CHECK (review_step IN ('student_submit', 'club_review', 'advisor_review', 'school_review', 'publicity', 'archive')),
  CONSTRAINT CK_AWARD_REVIEWS_RESULT CHECK (review_result IN ('submit', 'approve', 'reject', 'return', 'publish', 'archive', 'withdraw'))
);

CREATE INDEX IX_AWARD_REVIEWS_APPLICATION ON AWARD_REVIEW_RECORDS (award_application_id, review_round, reviewed_at);

CREATE TABLE AWARD_ATTACHMENTS (
  attachment_id number DEFAULT SEQ_AWARD_ATTACHMENTS.NEXTVAL PRIMARY KEY,
  award_application_id number NOT NULL,
  attachment_name varchar2(255 char) NOT NULL,
  attachment_url varchar2(1000 char) NOT NULL,
  attachment_type varchar2(100 char),
  uploaded_by_user_id number NOT NULL,
  uploaded_at date DEFAULT SYSDATE NOT NULL
);

CREATE INDEX IX_AWARD_ATTACHMENTS_APP ON AWARD_ATTACHMENTS (award_application_id, uploaded_at);

CREATE TABLE AWARD_PUBLICITY_BATCHES (
  publicity_batch_id number DEFAULT SEQ_AWARD_PUBLICITY_BATCHES.NEXTVAL PRIMARY KEY,
  club_id number NOT NULL,
  title varchar2(255 char) NOT NULL,
  description clob,
  publicity_start_at date,
  publicity_end_at date,
  publicity_status varchar2(30 char) DEFAULT 'draft' NOT NULL,
  publisher_user_id number,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_AWARD_PUBLICITY_BATCH_SCOPE UNIQUE (club_id, publicity_batch_id),
  CONSTRAINT CK_AWARD_PUBLICITY_STATUS CHECK (publicity_status IN ('draft', 'publicizing', 'closed', 'archived'))
);

CREATE INDEX IX_AWARD_PUBLICITY_CLUB ON AWARD_PUBLICITY_BATCHES (club_id, publicity_status, publicity_start_at);

CREATE TABLE AWARD_PUBLICITY_ITEMS (
  publicity_item_id number DEFAULT SEQ_AWARD_PUBLICITY_ITEMS.NEXTVAL PRIMARY KEY,
  publicity_batch_id number NOT NULL,
  club_id number NOT NULL,
  award_application_id number NOT NULL,
  display_order number DEFAULT 0 NOT NULL,
  publicity_result varchar2(30 char) DEFAULT 'normal' NOT NULL,
  created_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT UQ_AWARD_PUBLICITY_ITEMS_APP UNIQUE (publicity_batch_id, award_application_id),
  CONSTRAINT CK_AWARD_PUBLICITY_ITEMS_RESULT CHECK (publicity_result IN ('normal', 'withdrawn', 'corrected'))
);

CREATE INDEX IX_AWARD_PUBLICITY_ITEMS_ORDER ON AWARD_PUBLICITY_ITEMS (publicity_batch_id, display_order);

CREATE TABLE AWARD_RULE_DOCUMENTS (
  rule_document_id number DEFAULT SEQ_AWARD_RULE_DOCUMENTS.NEXTVAL PRIMARY KEY,
  club_id number,
  rule_title varchar2(255 char) NOT NULL,
  rule_scope varchar2(30 char) DEFAULT 'club' NOT NULL,
  academic_year varchar2(50 char) NOT NULL,
  term_name varchar2(80 char),
  issuer_name varchar2(255 char),
  summary clob,
  content_text clob,
  material_url varchar2(1000 char),
  material_name varchar2(255 char),
  version_no varchar2(50 char) DEFAULT '1.0' NOT NULL,
  rule_status varchar2(30 char) DEFAULT 'draft' NOT NULL,
  effective_start_at date,
  effective_end_at date,
  published_by_user_id number,
  published_at date,
  created_at date DEFAULT SYSDATE NOT NULL,
  updated_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT CK_AWARD_RULE_DOCS_SCOPE CHECK (rule_scope IN ('global', 'club')),
  CONSTRAINT CK_AWARD_RULE_DOCS_STATUS CHECK (rule_status IN ('draft', 'published', 'archived')),
  CONSTRAINT CK_AWARD_RULE_DOCS_CLUB CHECK (
    (rule_scope = 'global' AND club_id IS NULL) OR
    (rule_scope = 'club' AND club_id IS NOT NULL)
  )
);

CREATE INDEX IX_AWARD_RULE_DOCS_SCOPE ON AWARD_RULE_DOCUMENTS (club_id, rule_status, academic_year, term_name);

CREATE INDEX IX_AWARD_RULE_DOCS_STATUS ON AWARD_RULE_DOCUMENTS (rule_scope, rule_status, effective_start_at);

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
  created_at date,
  CONSTRAINT UQ_EVALUATIONS_SOURCE_SCOPE UNIQUE (club_id, user_id, evaluation_id)
);

CREATE TABLE EVALUATION_AWARD_SOURCES (
  club_id number NOT NULL,
  user_id number NOT NULL,
  evaluation_id number NOT NULL,
  award_application_id number NOT NULL,
  award_score number DEFAULT 0 NOT NULL,
  created_at date DEFAULT SYSDATE NOT NULL,
  CONSTRAINT PK_EVALUATION_AWARD_SOURCES PRIMARY KEY (evaluation_id, award_application_id),
  CONSTRAINT CK_EVALUATION_AWARD_SOURCES_SCORE CHECK (award_score BETWEEN 0 AND 100)
);

CREATE TABLE NOTICES (
  notice_id number DEFAULT SEQ_NOTICES.NEXTVAL PRIMARY KEY,
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
  read_id number DEFAULT SEQ_NOTICE_READS.NEXTVAL PRIMARY KEY,
  notice_id number,
  user_id number,
  read_at date,
  CONSTRAINT UQ_NOTICE_READS_NOTICE_USER UNIQUE (notice_id, user_id)
);

CREATE TABLE FORUM_POSTS (
  post_id number DEFAULT SEQ_FORUM_POSTS.NEXTVAL PRIMARY KEY,
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
  log_id number DEFAULT SEQ_OPERATION_LOGS.NEXTVAL PRIMARY KEY,
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

ALTER TABLE CLUB_DEPARTMENTS ADD CONSTRAINT FK_CLUB_DEPARTMENTS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_GROUPS ADD CONSTRAINT FK_CLUB_GROUPS_DEPARTMENT FOREIGN KEY (club_id, department_id) REFERENCES CLUB_DEPARTMENTS (club_id, department_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_MEMBERS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_MEMBERS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_MEMBERS ADD CONSTRAINT CK_CLUB_MEMBERS_GROUP_DEPT CHECK (group_id IS NULL OR department_id IS NOT NULL);

ALTER TABLE CLUB_MEMBERS ADD CONSTRAINT FK_CLUB_MEMBERS_DEPARTMENT FOREIGN KEY (club_id, department_id) REFERENCES CLUB_DEPARTMENTS (club_id, department_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE CLUB_MEMBERS ADD CONSTRAINT FK_CLUB_MEMBERS_GROUP FOREIGN KEY (club_id, department_id, group_id) REFERENCES CLUB_GROUPS (club_id, department_id, group_id) DEFERRABLE INITIALLY IMMEDIATE;

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

ALTER TABLE AWARD_SCHEMES ADD CONSTRAINT FK_AWARD_SCHEMES_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_SCHEMES ADD CONSTRAINT FK_AWARD_SCHEMES_CREATOR FOREIGN KEY (created_by_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_LEVELS ADD CONSTRAINT FK_AWARD_LEVELS_SCHEME FOREIGN KEY (award_scheme_id) REFERENCES AWARD_SCHEMES (award_scheme_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_SCHEME FOREIGN KEY (club_id, award_scheme_id) REFERENCES AWARD_SCHEMES (club_id, award_scheme_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_LEVEL FOREIGN KEY (award_scheme_id, award_level_id) REFERENCES AWARD_LEVELS (award_scheme_id, award_level_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_APPLICANT FOREIGN KEY (applicant_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_RECOMMENDER FOREIGN KEY (recommender_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_SUBMITTER FOREIGN KEY (submitter_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_REVIEW_RECORDS ADD CONSTRAINT FK_AWARD_REVIEWS_APPLICATION FOREIGN KEY (award_application_id) REFERENCES AWARD_APPLICATIONS (award_application_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_REVIEW_RECORDS ADD CONSTRAINT FK_AWARD_REVIEWS_REVIEWER FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_ATTACHMENTS ADD CONSTRAINT FK_AWARD_ATTACHMENTS_APPLICATION FOREIGN KEY (award_application_id) REFERENCES AWARD_APPLICATIONS (award_application_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_ATTACHMENTS ADD CONSTRAINT FK_AWARD_ATTACHMENTS_UPLOADER FOREIGN KEY (uploaded_by_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_PUBLICITY_BATCHES ADD CONSTRAINT FK_AWARD_PUBLICITY_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_PUBLICITY_BATCHES ADD CONSTRAINT FK_AWARD_PUBLICITY_PUBLISHER FOREIGN KEY (publisher_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_PUBLICITY_ITEMS ADD CONSTRAINT FK_AWARD_PUBLICITY_ITEMS_BATCH FOREIGN KEY (club_id, publicity_batch_id) REFERENCES AWARD_PUBLICITY_BATCHES (club_id, publicity_batch_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_PUBLICITY_ITEMS ADD CONSTRAINT FK_AWARD_PUBLICITY_ITEMS_APP FOREIGN KEY (club_id, award_application_id) REFERENCES AWARD_APPLICATIONS (club_id, award_application_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_RULE_DOCUMENTS ADD CONSTRAINT FK_AWARD_RULE_DOCS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE AWARD_RULE_DOCUMENTS ADD CONSTRAINT FK_AWARD_RULE_DOCS_PUBLISHER FOREIGN KEY (published_by_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATIONS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATIONS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATIONS ADD FOREIGN KEY (evaluator_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATION_AWARD_SOURCES ADD CONSTRAINT FK_EAS_EVALUATION FOREIGN KEY (club_id, user_id, evaluation_id) REFERENCES EVALUATIONS (club_id, user_id, evaluation_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE EVALUATION_AWARD_SOURCES ADD CONSTRAINT FK_EAS_APPLICATION FOREIGN KEY (club_id, user_id, award_application_id) REFERENCES AWARD_APPLICATIONS (club_id, applicant_user_id, award_application_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICES ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICES ADD FOREIGN KEY (publisher_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICE_READS ADD FOREIGN KEY (notice_id) REFERENCES NOTICES (notice_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE NOTICE_READS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE FORUM_POSTS ADD FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE FORUM_POSTS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE FORUM_POSTS ADD FOREIGN KEY (parent_post_id) REFERENCES FORUM_POSTS (post_id) DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE OPERATION_LOGS ADD FOREIGN KEY (user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE;
