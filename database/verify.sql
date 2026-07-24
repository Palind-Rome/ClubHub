SELECT USER AS current_user FROM dual;

SELECT COUNT(*) AS table_count
FROM user_tables;

SELECT table_name
FROM user_tables
ORDER BY table_name;

SELECT sequence_name, last_number
FROM user_sequences
WHERE sequence_name IN (
  'SEQ_USERS',
  'SEQ_USER_ROLES',
  'SEQ_CLUBS',
  'SEQ_CLUB_MEMBERS',
  'SEQ_CLUB_DEPARTMENTS',
  'SEQ_CLUB_GROUPS',
  'SEQ_AWARD_SCHEMES',
  'SEQ_AWARD_LEVELS',
  'SEQ_AWARD_APPLICATIONS',
  'SEQ_AWARD_REVIEW_RECORDS',
  'SEQ_AWARD_ATTACHMENTS',
  'SEQ_AWARD_PUBLICITY_BATCHES',
  'SEQ_AWARD_PUBLICITY_ITEMS',
  'SEQ_AWARD_RULE_DOCUMENTS',
  'SEQ_EVALUATIONS',
  'SEQ_ACTIVITIES',
  'SEQ_ACTIVITY_PARTICIPATIONS',
  'SEQ_BUDGET_ACCOUNTS',
  'SEQ_BUDGET_APPLICATIONS',
  'SEQ_BUDGET_REVIEW_RECORDS',
  'SEQ_BUDGET_TRANSACTIONS',
  'SEQ_NOTICE_READS',
  'SEQ_ROLES',
  'SEQ_RECRUITMENTS',
  'SEQ_RECRUITMENT_APPLICATIONS',
  'SEQ_VENUES',
  'SEQ_VENUE_RESERVATIONS',
  'SEQ_PROJECTS',
  'SEQ_PROJECT_MEMBERS',
  'SEQ_PROJECT_TASKS',
  'SEQ_PROJECT_TASK_ASSIGNEES',
  'SEQ_PROJECT_TASK_PROGRESS_REPORTS',
  'SEQ_LEARNING_ITEMS',
  'SEQ_LEARNING_RECORDS',
  'SEQ_MATERIALS',
  'SEQ_MATERIAL_BORROWS',
  'SEQ_NOTICES',
  'SEQ_FORUM_POSTS',
  'SEQ_OPERATION_LOGS'
)
ORDER BY sequence_name;

SELECT index_name, table_name, uniqueness
FROM user_indexes
WHERE index_name IN (
  'UQ_USERS_USERNAME',
  'UQ_USERS_STUDENT_NO',
  'UQ_USER_ROLES_SCOPE',
  'UQ_BUDGET_ACCOUNTS_SCOPE',
  'UQ_BUDGET_ACCOUNTS_YEAR',
  'UQ_BUDGET_APPLICATIONS_ACCOUNT_SCOPE',
  'UQ_BUDGET_APPLICATIONS_SCOPE',
  'UQ_BUDGET_TXN_APPLICATION',
  'UQ_NOTICE_READS_NOTICE_USER'
)
ORDER BY index_name;

SELECT table_name, column_name, data_default
FROM user_tab_columns
WHERE (table_name, column_name) IN (
  ('USERS', 'USER_ID'),
  ('USER_ROLES', 'USER_ROLE_ID'),
  ('CLUBS', 'CLUB_ID'),
  ('CLUB_MEMBERS', 'MEMBER_ID'),
  ('CLUB_DEPARTMENTS', 'DEPARTMENT_ID'),
  ('CLUB_GROUPS', 'GROUP_ID'),
  ('AWARD_SCHEMES', 'AWARD_SCHEME_ID'),
  ('AWARD_LEVELS', 'AWARD_LEVEL_ID'),
  ('AWARD_APPLICATIONS', 'AWARD_APPLICATION_ID'),
  ('AWARD_REVIEW_RECORDS', 'REVIEW_ID'),
  ('AWARD_ATTACHMENTS', 'ATTACHMENT_ID'),
  ('AWARD_PUBLICITY_BATCHES', 'PUBLICITY_BATCH_ID'),
  ('AWARD_PUBLICITY_ITEMS', 'PUBLICITY_ITEM_ID'),
  ('AWARD_RULE_DOCUMENTS', 'RULE_DOCUMENT_ID'),
  ('EVALUATIONS', 'EVALUATION_ID'),
  ('ACTIVITIES', 'ACTIVITY_ID'),
  ('ACTIVITY_PARTICIPATIONS', 'PARTICIPATION_ID'),
  ('BUDGET_ACCOUNTS', 'ACCOUNT_ID'),
  ('BUDGET_APPLICATIONS', 'APPLICATION_ID'),
  ('BUDGET_REVIEW_RECORDS', 'REVIEW_ID'),
  ('BUDGET_TRANSACTIONS', 'TRANSACTION_ID'),
  ('NOTICE_READS', 'READ_ID'),
  ('ROLES', 'ROLE_ID'),
  ('RECRUITMENTS', 'RECRUIT_ID'),
  ('RECRUITMENT_APPLICATIONS', 'APPLICATION_ID'),
  ('VENUES', 'VENUE_ID'),
  ('VENUE_RESERVATIONS', 'RESERVATION_ID'),
  ('PROJECTS', 'PROJECT_ID'),
  ('PROJECT_MEMBERS', 'PROJECT_MEMBER_ID'),
  ('PROJECT_TASKS', 'TASK_ID'),
  ('PROJECT_TASK_ASSIGNEES', 'TASK_ASSIGNEE_ID'),
  ('PROJECT_TASK_PROGRESS_REPORTS', 'TASK_PROGRESS_REPORT_ID'),
  ('LEARNING_ITEMS', 'ITEM_ID'),
  ('LEARNING_RECORDS', 'RECORD_ID'),
  ('MATERIALS', 'MATERIAL_ID'),
  ('MATERIAL_BORROWS', 'BORROW_ID'),
  ('NOTICES', 'NOTICE_ID'),
  ('FORUM_POSTS', 'POST_ID'),
  ('OPERATION_LOGS', 'LOG_ID')
)
ORDER BY table_name, column_name;

-- 以下查询应返回 0 行：每个主键 sequence 必须存在，且下一个值必须大于当前最大主键。
WITH sequence_targets AS (
  SELECT 'SEQ_USERS' AS sequence_name, NVL(MAX(user_id), 0) AS max_id FROM users
  UNION ALL SELECT 'SEQ_USER_ROLES', NVL(MAX(user_role_id), 0) FROM user_roles
  UNION ALL SELECT 'SEQ_CLUBS', NVL(MAX(club_id), 0) FROM clubs
  UNION ALL SELECT 'SEQ_CLUB_MEMBERS', NVL(MAX(member_id), 0) FROM club_members
  UNION ALL SELECT 'SEQ_CLUB_DEPARTMENTS', NVL(MAX(department_id), 0) FROM club_departments
  UNION ALL SELECT 'SEQ_CLUB_GROUPS', NVL(MAX(group_id), 0) FROM club_groups
  UNION ALL SELECT 'SEQ_AWARD_SCHEMES', NVL(MAX(award_scheme_id), 0) FROM award_schemes
  UNION ALL SELECT 'SEQ_AWARD_LEVELS', NVL(MAX(award_level_id), 0) FROM award_levels
  UNION ALL SELECT 'SEQ_AWARD_APPLICATIONS', NVL(MAX(award_application_id), 0) FROM award_applications
  UNION ALL SELECT 'SEQ_AWARD_REVIEW_RECORDS', NVL(MAX(review_id), 0) FROM award_review_records
  UNION ALL SELECT 'SEQ_AWARD_ATTACHMENTS', NVL(MAX(attachment_id), 0) FROM award_attachments
  UNION ALL SELECT 'SEQ_AWARD_PUBLICITY_BATCHES', NVL(MAX(publicity_batch_id), 0) FROM award_publicity_batches
  UNION ALL SELECT 'SEQ_AWARD_PUBLICITY_ITEMS', NVL(MAX(publicity_item_id), 0) FROM award_publicity_items
  UNION ALL SELECT 'SEQ_AWARD_RULE_DOCUMENTS', NVL(MAX(rule_document_id), 0) FROM award_rule_documents
  UNION ALL SELECT 'SEQ_EVALUATIONS', NVL(MAX(evaluation_id), 0) FROM evaluations
  UNION ALL SELECT 'SEQ_ACTIVITIES', NVL(MAX(activity_id), 0) FROM activities
  UNION ALL SELECT 'SEQ_ACTIVITY_PARTICIPATIONS', NVL(MAX(participation_id), 0) FROM activity_participations
  UNION ALL SELECT 'SEQ_BUDGET_ACCOUNTS', NVL(MAX(account_id), 0) FROM budget_accounts
  UNION ALL SELECT 'SEQ_BUDGET_APPLICATIONS', NVL(MAX(application_id), 0) FROM budget_applications
  UNION ALL SELECT 'SEQ_BUDGET_REVIEW_RECORDS', NVL(MAX(review_id), 0) FROM budget_review_records
  UNION ALL SELECT 'SEQ_BUDGET_TRANSACTIONS', NVL(MAX(transaction_id), 0) FROM budget_transactions
  UNION ALL SELECT 'SEQ_NOTICE_READS', NVL(MAX(read_id), 0) FROM notice_reads
  UNION ALL SELECT 'SEQ_ROLES', NVL(MAX(role_id), 0) FROM roles
  UNION ALL SELECT 'SEQ_RECRUITMENTS', NVL(MAX(recruit_id), 0) FROM recruitments
  UNION ALL SELECT 'SEQ_RECRUITMENT_APPLICATIONS', NVL(MAX(application_id), 0) FROM recruitment_applications
  UNION ALL SELECT 'SEQ_VENUES', NVL(MAX(venue_id), 0) FROM venues
  UNION ALL SELECT 'SEQ_VENUE_RESERVATIONS', NVL(MAX(reservation_id), 0) FROM venue_reservations
  UNION ALL SELECT 'SEQ_PROJECTS', NVL(MAX(project_id), 0) FROM projects
  UNION ALL SELECT 'SEQ_PROJECT_MEMBERS', NVL(MAX(project_member_id), 0) FROM project_members
  UNION ALL SELECT 'SEQ_PROJECT_TASKS', NVL(MAX(task_id), 0) FROM project_tasks
  UNION ALL SELECT 'SEQ_PROJECT_TASK_ASSIGNEES', NVL(MAX(task_assignee_id), 0) FROM project_task_assignees
  UNION ALL SELECT 'SEQ_PROJECT_TASK_PROGRESS_REPORTS', NVL(MAX(task_progress_report_id), 0) FROM project_task_progress_reports
  UNION ALL SELECT 'SEQ_LEARNING_ITEMS', NVL(MAX(item_id), 0) FROM learning_items
  UNION ALL SELECT 'SEQ_LEARNING_RECORDS', NVL(MAX(record_id), 0) FROM learning_records
  UNION ALL SELECT 'SEQ_MATERIALS', NVL(MAX(material_id), 0) FROM materials
  UNION ALL SELECT 'SEQ_MATERIAL_BORROWS', NVL(MAX(borrow_id), 0) FROM material_borrows
  UNION ALL SELECT 'SEQ_NOTICES', NVL(MAX(notice_id), 0) FROM notices
  UNION ALL SELECT 'SEQ_FORUM_POSTS', NVL(MAX(post_id), 0) FROM forum_posts
  UNION ALL SELECT 'SEQ_OPERATION_LOGS', NVL(MAX(log_id), 0) FROM operation_logs
)
SELECT target.sequence_name, target.max_id, sequence_state.last_number
FROM sequence_targets target
LEFT JOIN user_sequences sequence_state
  ON sequence_state.sequence_name = target.sequence_name
WHERE sequence_state.sequence_name IS NULL
   OR sequence_state.last_number <= target.max_id
ORDER BY target.sequence_name;

-- ClubHub 核心表应为 40 张；使用固定集合计数，避免测试 schema 中的临时表干扰结果。
SELECT COUNT(*) AS clubhub_core_table_count
FROM user_tables
WHERE table_name IN (
  'USERS', 'ROLES', 'USER_ROLES',
  'CLUBS', 'CLUB_DEPARTMENTS', 'CLUB_GROUPS', 'CLUB_MEMBERS', 'RECRUITMENTS', 'RECRUITMENT_APPLICATIONS',
  'ACTIVITIES', 'ACTIVITY_PARTICIPATIONS', 'VENUES', 'VENUE_RESERVATIONS',
  'BUDGET_ACCOUNTS', 'BUDGET_APPLICATIONS', 'BUDGET_REVIEW_RECORDS', 'BUDGET_TRANSACTIONS',
  'PROJECTS', 'PROJECT_MEMBERS', 'PROJECT_TASKS', 'PROJECT_TASK_ASSIGNEES', 'PROJECT_TASK_PROGRESS_REPORTS',
  'LEARNING_ITEMS', 'LEARNING_RECORDS',
  'MATERIALS', 'MATERIAL_BORROWS',
  'AWARD_SCHEMES', 'AWARD_LEVELS', 'AWARD_APPLICATIONS', 'AWARD_REVIEW_RECORDS',
  'AWARD_ATTACHMENTS', 'AWARD_PUBLICITY_BATCHES', 'AWARD_PUBLICITY_ITEMS', 'AWARD_RULE_DOCUMENTS',
  'EVALUATIONS', 'EVALUATION_AWARD_SOURCES',
  'NOTICES', 'NOTICE_READS', 'FORUM_POSTS', 'OPERATION_LOGS'
);

SELECT column_id, column_name, data_type, data_length, nullable, data_default
FROM user_tab_columns
WHERE table_name = 'PROJECT_MEMBERS'
ORDER BY column_id;

SELECT constraint_name, constraint_type, status, deferrable, deferred
FROM user_constraints
WHERE table_name = 'PROJECT_MEMBERS'
ORDER BY constraint_type, constraint_name;

SELECT index_name, uniqueness
FROM user_indexes
WHERE index_name = 'UQ_PM_ACTIVE_LEADER';

-- 以下查询均应返回 0 行。
SELECT project_id, user_id, COUNT(*) AS duplicate_count
FROM project_members
GROUP BY project_id, user_id
HAVING COUNT(*) > 1;

SELECT notice_id, user_id, COUNT(*) AS duplicate_count
FROM notice_reads
GROUP BY notice_id, user_id
HAVING COUNT(*) > 1;

SELECT club_id, fiscal_year, COUNT(*) AS duplicate_count
FROM budget_accounts
GROUP BY club_id, fiscal_year
HAVING COUNT(*) > 1;

SELECT application_id, transaction_type, COUNT(*) AS duplicate_count
FROM budget_transactions
WHERE application_id IS NOT NULL
GROUP BY application_id, transaction_type
HAVING COUNT(*) > 1;

SELECT account_id, fiscal_year, initial_amount, account_status
FROM budget_accounts
WHERE initial_amount < 0
   OR account_status NOT IN ('active', 'closed');

SELECT application_id, application_type, amount, application_status
FROM budget_applications
WHERE application_type NOT IN ('activity_budget', 'purchase', 'reimbursement')
   OR amount <= 0
   OR application_status NOT IN ('pending', 'approved', 'rejected', 'cancelled');

SELECT transaction_id, transaction_type, amount
FROM budget_transactions
WHERE transaction_type NOT IN ('commitment', 'expense', 'refund', 'adjustment')
   OR amount = 0;

SELECT account.account_id,
       account.club_id,
       account.fiscal_year,
       account.initial_amount + NVL(SUM(txn.amount), 0) AS remaining_amount
FROM budget_accounts account
LEFT JOIN budget_transactions txn
  ON txn.club_id = account.club_id
 AND txn.account_id = account.account_id
GROUP BY account.account_id, account.club_id, account.fiscal_year, account.initial_amount
HAVING account.initial_amount + NVL(SUM(txn.amount), 0) < 0;

SELECT txn.transaction_id,
       txn.club_id AS transaction_club_id,
       txn.account_id AS transaction_account_id,
       app.club_id AS application_club_id,
       app.account_id AS application_account_id
FROM budget_transactions txn
JOIN budget_applications app
  ON app.application_id = txn.application_id
WHERE txn.application_id IS NOT NULL
  AND (txn.club_id <> app.club_id OR txn.account_id <> app.account_id);

SELECT app.application_id, app.application_status
FROM budget_applications app
WHERE application_status = 'approved'
  AND NOT EXISTS (
    SELECT 1
    FROM budget_transactions txn
    WHERE txn.club_id = app.club_id
      AND txn.account_id = app.account_id
      AND txn.application_id = app.application_id
      AND txn.transaction_type = 'commitment'
      AND txn.amount = -app.amount
  );

SELECT project_member_id, member_role, member_status
FROM project_members
WHERE member_role NOT IN ('leader', 'member', 'mentor')
   OR member_status NOT IN ('active', 'removed', 'quit');

SELECT p.project_id, p.leader_user_id
FROM projects p
WHERE p.leader_user_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM project_members pm
    WHERE pm.project_id = p.project_id
      AND pm.user_id = p.leader_user_id
      AND pm.member_role = 'leader'
      AND pm.member_status = 'active'
  );

SELECT project_id, COUNT(*) AS active_leader_count
FROM project_members
WHERE member_role = 'leader' AND member_status = 'active'
GROUP BY project_id
HAVING COUNT(*) > 1;

SELECT task_id, user_id, COUNT(*) AS duplicate_count
FROM project_task_assignees
GROUP BY task_id, user_id
HAVING COUNT(*) > 1;

SELECT task_progress_report_id, progress, task_status
FROM project_task_progress_reports
WHERE progress NOT BETWEEN 0 AND 100
   OR task_status NOT IN ('pending', 'in_progress', 'completed', 'delayed');

SELECT column_id, column_name, data_type, nullable, data_default
FROM user_tab_columns
WHERE table_name IN ('CLUB_DEPARTMENTS', 'CLUB_GROUPS')
ORDER BY table_name, column_id;

SELECT column_id, column_name, data_type, nullable
FROM user_tab_columns
WHERE table_name = 'CLUB_MEMBERS'
  AND column_name IN ('DEPARTMENT_ID', 'GROUP_ID', 'DEPARTMENT_NAME', 'GROUP_NAME')
ORDER BY column_id;

SELECT constraint_name, table_name, constraint_type, status, deferrable, deferred
FROM user_constraints
WHERE constraint_name IN (
  'FK_CLUB_DEPARTMENTS_CLUB',
  'FK_CLUB_GROUPS_DEPARTMENT',
  'CK_CLUB_MEMBERS_GROUP_DEPT',
  'FK_CLUB_MEMBERS_DEPARTMENT',
  'FK_CLUB_MEMBERS_GROUP',
  'UQ_CLUB_DEPARTMENTS_SCOPE',
  'UQ_CLUB_DEPARTMENTS_NAME',
  'UQ_CLUB_GROUPS_SCOPE',
  'UQ_CLUB_GROUPS_DEPT_SCOPE',
  'UQ_CLUB_GROUPS_NAME'
)
ORDER BY table_name, constraint_type, constraint_name;

SELECT constraint_name,
       table_name,
       LISTAGG(column_name, ',') WITHIN GROUP (ORDER BY position) AS constraint_columns
FROM user_cons_columns
WHERE constraint_name IN (
  'FK_AWARD_APPLICATIONS_SCHEME',
  'FK_AWARD_APPLICATIONS_LEVEL',
  'FK_AWARD_PUBLICITY_ITEMS_APP',
  'FK_AWARD_PUBLICITY_ITEMS_BATCH'
)
GROUP BY constraint_name, table_name
ORDER BY constraint_name;

SELECT index_name, table_name, uniqueness
FROM user_indexes
WHERE index_name IN (
  'IX_CLUB_DEPARTMENTS_ORDER',
  'IX_CLUB_GROUPS_ORDER',
  'IX_CLUB_MEMBERS_ORG'
)
ORDER BY index_name;

-- 以下查询均应返回 0 行。
SELECT club_id, department_name, COUNT(*) AS duplicate_count
FROM club_departments
GROUP BY club_id, department_name
HAVING COUNT(*) > 1;

SELECT g.group_id, g.club_id, g.department_id
FROM club_groups g
WHERE NOT EXISTS (
  SELECT 1
  FROM club_departments d
  WHERE d.club_id = g.club_id
    AND d.department_id = g.department_id
);

SELECT club_id, department_id, group_name, COUNT(*) AS duplicate_count
FROM club_groups
GROUP BY club_id, department_id, group_name
HAVING COUNT(*) > 1;

SELECT member_id, club_id, department_name
FROM club_members
WHERE department_name IS NOT NULL
  AND TRIM(department_name) IS NOT NULL
  AND department_id IS NULL;

SELECT member_id, club_id, department_id, group_name
FROM club_members
WHERE group_name IS NOT NULL
  AND TRIM(group_name) IS NOT NULL
  AND group_id IS NULL;

SELECT member_id, club_id, department_id, group_id
FROM club_members
WHERE group_id IS NOT NULL
  AND department_id IS NULL;

SELECT cm.member_id, cm.club_id, cm.department_id
FROM club_members cm
WHERE cm.department_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM club_departments d
    WHERE d.club_id = cm.club_id
      AND d.department_id = cm.department_id
  );

SELECT cm.member_id, cm.club_id, cm.department_id, cm.group_id
FROM club_members cm
WHERE cm.group_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM club_groups g
    WHERE g.club_id = cm.club_id
      AND g.department_id = cm.department_id
      AND g.group_id = cm.group_id
  );

SELECT column_id, column_name, data_type, nullable, data_default
FROM user_tab_columns
WHERE table_name IN (
  'AWARD_SCHEMES',
  'AWARD_LEVELS',
  'AWARD_APPLICATIONS',
  'AWARD_REVIEW_RECORDS',
  'AWARD_ATTACHMENTS',
  'AWARD_PUBLICITY_BATCHES',
  'AWARD_PUBLICITY_ITEMS',
  'AWARD_RULE_DOCUMENTS',
  'EVALUATION_AWARD_SOURCES'
)
ORDER BY table_name, column_id;

SELECT constraint_name, table_name, constraint_type, status, deferrable, deferred
FROM user_constraints
WHERE constraint_name IN (
  'UQ_AWARD_SCHEMES_SCOPE',
  'UQ_AWARD_LEVELS_SCOPE',
  'UQ_AWARD_LEVELS_NAME',
  'UQ_AWARD_APPLICATIONS_SCOPE',
  'UQ_AWARD_APPLICATIONS_MEMBER_SCOPE',
  'UQ_AWARD_APPLICATIONS_APPLICANT',
  'UQ_AWARD_PUBLICITY_BATCH_SCOPE',
  'UQ_AWARD_PUBLICITY_ITEMS_APP',
  'UQ_EVALUATIONS_SOURCE_SCOPE',
  'PK_EVALUATION_AWARD_SOURCES',
  'FK_AWARD_SCHEMES_CLUB',
  'FK_AWARD_SCHEMES_CREATOR',
  'FK_AWARD_LEVELS_SCHEME',
  'FK_AWARD_APPLICATIONS_CLUB',
  'FK_AWARD_APPLICATIONS_SCHEME',
  'FK_AWARD_APPLICATIONS_LEVEL',
  'FK_AWARD_APPLICATIONS_APPLICANT',
  'FK_AWARD_APPLICATIONS_RECOMMENDER',
  'FK_AWARD_APPLICATIONS_SUBMITTER',
  'FK_AWARD_REVIEWS_APPLICATION',
  'FK_AWARD_REVIEWS_REVIEWER',
  'FK_AWARD_ATTACHMENTS_APPLICATION',
  'FK_AWARD_ATTACHMENTS_UPLOADER',
  'FK_AWARD_PUBLICITY_CLUB',
  'FK_AWARD_PUBLICITY_PUBLISHER',
  'FK_AWARD_PUBLICITY_ITEMS_BATCH',
  'FK_AWARD_PUBLICITY_ITEMS_APP',
  'FK_AWARD_RULE_DOCS_CLUB',
  'FK_AWARD_RULE_DOCS_PUBLISHER',
  'FK_EAS_EVALUATION',
  'FK_EAS_APPLICATION'
)
ORDER BY table_name, constraint_type, constraint_name;

SELECT index_name, table_name, uniqueness
FROM user_indexes
WHERE index_name IN (
  'UQ_AWARD_SCHEMES_NAME',
  'IX_AWARD_SCHEMES_CLUB_STATUS',
  'IX_AWARD_LEVELS_ORDER',
  'IX_AWARD_APPLICATIONS_STATUS',
  'IX_AWARD_APPLICATIONS_USER',
  'IX_AWARD_REVIEWS_APPLICATION',
  'IX_AWARD_ATTACHMENTS_APP',
  'IX_AWARD_PUBLICITY_CLUB',
  'IX_AWARD_PUBLICITY_ITEMS_ORDER',
  'IX_AWARD_RULE_DOCS_SCOPE',
  'IX_AWARD_RULE_DOCS_STATUS'
)
ORDER BY index_name;

-- The following award workflow checks should return 0 rows.
WITH expected_constraint_columns AS (
  SELECT 'FK_AWARD_APPLICATIONS_SCHEME' AS constraint_name,
         'CLUB_ID,AWARD_SCHEME_ID' AS expected_columns
  FROM dual
  UNION ALL
  SELECT 'FK_AWARD_APPLICATIONS_LEVEL',
         'AWARD_SCHEME_ID,AWARD_LEVEL_ID'
  FROM dual
  UNION ALL
  SELECT 'FK_EAS_EVALUATION',
         'CLUB_ID,USER_ID,EVALUATION_ID'
  FROM dual
  UNION ALL
  SELECT 'FK_EAS_APPLICATION',
         'CLUB_ID,USER_ID,AWARD_APPLICATION_ID'
  FROM dual
),
actual_constraint_columns AS (
  SELECT constraint_name,
         LISTAGG(column_name, ',') WITHIN GROUP (ORDER BY position) AS actual_columns
  FROM user_cons_columns
  WHERE constraint_name IN (
    'FK_AWARD_APPLICATIONS_SCHEME',
    'FK_AWARD_APPLICATIONS_LEVEL',
    'FK_EAS_EVALUATION',
    'FK_EAS_APPLICATION'
  )
  GROUP BY constraint_name
)
SELECT expected.constraint_name,
       expected.expected_columns,
       actual.actual_columns
FROM expected_constraint_columns expected
LEFT JOIN actual_constraint_columns actual
  ON actual.constraint_name = expected.constraint_name
WHERE actual.actual_columns IS NULL
   OR actual.actual_columns <> expected.expected_columns;

SELECT award_scheme_id, level_name, COUNT(*) AS duplicate_count
FROM award_levels
GROUP BY award_scheme_id, level_name
HAVING COUNT(*) > 1;

SELECT award_scheme_id, applicant_user_id, COUNT(*) AS duplicate_count
FROM award_applications
GROUP BY award_scheme_id, applicant_user_id
HAVING COUNT(*) > 1;

SELECT application.award_application_id, application.club_id, application.award_scheme_id
FROM award_applications application
WHERE NOT EXISTS (
  SELECT 1
  FROM award_schemes scheme
  WHERE scheme.club_id = application.club_id
    AND scheme.award_scheme_id = application.award_scheme_id
);

SELECT application.award_application_id, application.award_scheme_id, application.award_level_id
FROM award_applications application
WHERE application.award_level_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM award_levels award_level
    WHERE award_level.award_scheme_id = application.award_scheme_id
      AND award_level.award_level_id = application.award_level_id
  );

SELECT item.publicity_item_id, item.club_id, item.publicity_batch_id
FROM award_publicity_items item
WHERE NOT EXISTS (
  SELECT 1
  FROM award_publicity_batches batch
  WHERE batch.club_id = item.club_id
    AND batch.publicity_batch_id = item.publicity_batch_id
);

SELECT item.publicity_item_id, item.club_id, item.award_application_id
FROM award_publicity_items item
WHERE NOT EXISTS (
  SELECT 1
  FROM award_applications application
  WHERE application.club_id = item.club_id
    AND application.award_application_id = item.award_application_id
);

SELECT source.club_id,
       source.user_id,
       source.evaluation_id,
       source.award_application_id,
       source.award_score,
       evaluation.club_id AS evaluation_club_id,
       evaluation.user_id AS evaluation_user_id,
       application.club_id AS application_club_id,
       application.applicant_user_id,
       application.application_status,
       application.public_status,
       application.final_award_score
FROM evaluation_award_sources source
LEFT JOIN evaluations evaluation
  ON evaluation.evaluation_id = source.evaluation_id
LEFT JOIN award_applications application
  ON application.award_application_id = source.award_application_id
WHERE evaluation.evaluation_id IS NULL
   OR application.award_application_id IS NULL
   OR source.club_id <> evaluation.club_id
   OR source.user_id <> evaluation.user_id
   OR source.club_id <> application.club_id
   OR source.user_id <> application.applicant_user_id
   OR application.application_status <> 'archived'
   OR application.public_status <> 'publicized'
   OR application.final_award_score IS NULL
   OR source.award_score <> application.final_award_score;

SELECT rule_document_id, rule_scope, club_id, rule_status
FROM award_rule_documents
WHERE rule_scope NOT IN ('global', 'club')
   OR rule_status NOT IN ('draft', 'published', 'archived')
   OR (rule_scope = 'global' AND club_id IS NOT NULL)
   OR (rule_scope = 'club' AND club_id IS NULL);

SELECT rule_document_id, club_id
FROM award_rule_documents document
WHERE document.club_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM clubs club
    WHERE club.club_id = document.club_id
  );

SELECT rule_document_id, published_by_user_id
FROM award_rule_documents document
WHERE document.published_by_user_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM users publisher
    WHERE publisher.user_id = document.published_by_user_id
  );
