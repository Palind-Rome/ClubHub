-- Harden evaluation award score provenance.
-- Adds club/user scope to EVALUATION_AWARD_SOURCES so a semester evaluation can
-- only reference approved award applications for the same club member.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  PROCEDURE add_column(
    p_table_name IN VARCHAR2,
    p_column_name IN VARCHAR2,
    p_ddl IN VARCHAR2
  ) IS
    existing_count NUMBER;
  BEGIN
    SELECT COUNT(*)
      INTO existing_count
      FROM user_tab_columns
     WHERE table_name = UPPER(p_table_name)
       AND column_name = UPPER(p_column_name);
    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE p_ddl;
    END IF;
  END;
BEGIN
  add_column(
    'EVALUATION_AWARD_SOURCES',
    'CLUB_ID',
    'ALTER TABLE EVALUATION_AWARD_SOURCES ADD (club_id NUMBER)');
  add_column(
    'EVALUATION_AWARD_SOURCES',
    'USER_ID',
    'ALTER TABLE EVALUATION_AWARD_SOURCES ADD (user_id NUMBER)');
END;
/

UPDATE EVALUATION_AWARD_SOURCES source
   SET (source.club_id, source.user_id) = (
     SELECT evaluation.club_id, evaluation.user_id
       FROM EVALUATIONS evaluation
      WHERE evaluation.evaluation_id = source.evaluation_id
   )
 WHERE (source.club_id IS NULL OR source.user_id IS NULL)
   AND EXISTS (
     SELECT 1
       FROM EVALUATIONS evaluation
      WHERE evaluation.evaluation_id = source.evaluation_id
   );

DECLARE
  invalid_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO invalid_count
    FROM EVALUATION_AWARD_SOURCES source
    LEFT JOIN EVALUATIONS evaluation
      ON evaluation.evaluation_id = source.evaluation_id
    LEFT JOIN AWARD_APPLICATIONS application
      ON application.award_application_id = source.award_application_id
   WHERE evaluation.evaluation_id IS NULL
      OR application.award_application_id IS NULL
      OR source.club_id IS NULL
      OR source.user_id IS NULL
      OR evaluation.club_id IS NULL
      OR evaluation.user_id IS NULL
      OR evaluation.club_id <> source.club_id
      OR evaluation.user_id <> source.user_id
      OR application.club_id <> source.club_id
      OR application.applicant_user_id <> source.user_id;

  IF invalid_count > 0 THEN
    raise_application_error(
      -20061,
      'EVALUATION_AWARD_SOURCES contains orphaned or cross-member award sources; clean data before hardening constraints.');
  END IF;
END;
/

ALTER TABLE EVALUATION_AWARD_SOURCES MODIFY (
  club_id NUMBER NOT NULL,
  user_id NUMBER NOT NULL
);

DECLARE
  PROCEDURE drop_constraint(
    p_constraint_name IN VARCHAR2
  ) IS
    existing_count NUMBER;
    target_table_name VARCHAR2(128);
  BEGIN
    SELECT COUNT(*)
      INTO existing_count
      FROM user_constraints
     WHERE constraint_name = UPPER(p_constraint_name);
    IF existing_count > 0 THEN
      SELECT table_name
        INTO target_table_name
        FROM user_constraints
       WHERE constraint_name = UPPER(p_constraint_name)
         AND ROWNUM = 1;
      EXECUTE IMMEDIATE 'ALTER TABLE ' || target_table_name || ' DROP CONSTRAINT ' || UPPER(p_constraint_name);
    END IF;
  END;

  PROCEDURE add_constraint(
    p_constraint_name IN VARCHAR2,
    p_ddl IN VARCHAR2
  ) IS
    existing_count NUMBER;
  BEGIN
    SELECT COUNT(*)
      INTO existing_count
      FROM user_constraints
     WHERE constraint_name = UPPER(p_constraint_name);
    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE p_ddl;
    END IF;
  END;
BEGIN
  drop_constraint('FK_EAS_EVALUATION');
  drop_constraint('FK_EAS_APPLICATION');

  add_constraint(
    'UQ_EVALUATIONS_SOURCE_SCOPE',
    'ALTER TABLE EVALUATIONS ADD CONSTRAINT UQ_EVALUATIONS_SOURCE_SCOPE UNIQUE (club_id, user_id, evaluation_id)');
  add_constraint(
    'UQ_AWARD_APPLICATIONS_MEMBER_SCOPE',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT UQ_AWARD_APPLICATIONS_MEMBER_SCOPE UNIQUE (club_id, applicant_user_id, award_application_id)');
  add_constraint(
    'FK_EAS_EVALUATION',
    'ALTER TABLE EVALUATION_AWARD_SOURCES ADD CONSTRAINT FK_EAS_EVALUATION FOREIGN KEY (club_id, user_id, evaluation_id) REFERENCES EVALUATIONS (club_id, user_id, evaluation_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint(
    'FK_EAS_APPLICATION',
    'ALTER TABLE EVALUATION_AWARD_SOURCES ADD CONSTRAINT FK_EAS_APPLICATION FOREIGN KEY (club_id, user_id, award_application_id) REFERENCES AWARD_APPLICATIONS (club_id, applicant_user_id, award_application_id) DEFERRABLE INITIALLY IMMEDIATE');
END;
/

COMMIT;
