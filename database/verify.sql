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
  'SEQ_EVALUATIONS',
  'SEQ_ACTIVITIES',
  'SEQ_ACTIVITY_PARTICIPATIONS'
)
ORDER BY sequence_name;

SELECT index_name, table_name, uniqueness
FROM user_indexes
WHERE index_name IN (
  'UQ_USERS_USERNAME',
  'UQ_USERS_STUDENT_NO',
  'UQ_USER_ROLES_SCOPE'
)
ORDER BY index_name;

SELECT table_name, column_name, data_default
FROM user_tab_columns
WHERE (table_name, column_name) IN (
  ('USERS', 'USER_ID'),
  ('USER_ROLES', 'USER_ROLE_ID'),
  ('CLUBS', 'CLUB_ID'),
  ('CLUB_MEMBERS', 'MEMBER_ID'),
  ('EVALUATIONS', 'EVALUATION_ID'),
  ('ACTIVITIES', 'ACTIVITY_ID'),
  ('ACTIVITY_PARTICIPATIONS', 'PARTICIPATION_ID')
)
ORDER BY table_name, column_name;
