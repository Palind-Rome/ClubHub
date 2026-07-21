-- Add award scheme, application, review, publicity and evaluation-source tables.
--
-- Rollback outline for a non-production test database:
-- 1. Drop FKs: FK_EAS_APPLICATION, FK_EAS_EVALUATION,
--    FK_AWARD_PUBLICITY_ITEMS_APP, FK_AWARD_PUBLICITY_ITEMS_BATCH,
--    FK_AWARD_PUBLICITY_PUBLISHER, FK_AWARD_PUBLICITY_CLUB,
--    FK_AWARD_ATTACHMENTS_UPLOADER, FK_AWARD_ATTACHMENTS_APPLICATION,
--    FK_AWARD_REVIEWS_REVIEWER, FK_AWARD_REVIEWS_APPLICATION,
--    FK_AWARD_APPLICATIONS_SUBMITTER, FK_AWARD_APPLICATIONS_RECOMMENDER,
--    FK_AWARD_APPLICATIONS_APPLICANT, FK_AWARD_APPLICATIONS_LEVEL,
--    FK_AWARD_APPLICATIONS_SCHEME, FK_AWARD_APPLICATIONS_CLUB,
--    FK_AWARD_LEVELS_SCHEME, FK_AWARD_SCHEMES_CREATOR, FK_AWARD_SCHEMES_CLUB.
-- 2. Drop tables in dependency order: EVALUATION_AWARD_SOURCES,
--    AWARD_PUBLICITY_ITEMS, AWARD_PUBLICITY_BATCHES, AWARD_ATTACHMENTS,
--    AWARD_REVIEW_RECORDS, AWARD_APPLICATIONS, AWARD_LEVELS, AWARD_SCHEMES.
-- 3. Drop SEQ_AWARD_* sequences.

DECLARE
  existing_count NUMBER;
BEGIN
  FOR item IN (
    SELECT 'SEQ_AWARD_SCHEMES' AS sequence_name FROM dual UNION ALL
    SELECT 'SEQ_AWARD_LEVELS' FROM dual UNION ALL
    SELECT 'SEQ_AWARD_APPLICATIONS' FROM dual UNION ALL
    SELECT 'SEQ_AWARD_REVIEW_RECORDS' FROM dual UNION ALL
    SELECT 'SEQ_AWARD_ATTACHMENTS' FROM dual UNION ALL
    SELECT 'SEQ_AWARD_PUBLICITY_BATCHES' FROM dual UNION ALL
    SELECT 'SEQ_AWARD_PUBLICITY_ITEMS' FROM dual
  ) LOOP
    SELECT COUNT(*) INTO existing_count
    FROM user_sequences
    WHERE sequence_name = item.sequence_name;

    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE 'CREATE SEQUENCE ' || item.sequence_name || ' START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE';
    END IF;
  END LOOP;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_SCHEMES';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_SCHEMES (
        award_scheme_id NUMBER DEFAULT SEQ_AWARD_SCHEMES.NEXTVAL PRIMARY KEY,
        club_id NUMBER NOT NULL,
        award_name VARCHAR2(255 CHAR) NOT NULL,
        award_category VARCHAR2(100 CHAR) DEFAULT 'honor' NOT NULL,
        academic_year VARCHAR2(50 CHAR) NOT NULL,
        term_name VARCHAR2(80 CHAR),
        sponsor_unit VARCHAR2(255 CHAR),
        reward_level VARCHAR2(100 CHAR),
        funding_source VARCHAR2(255 CHAR),
        is_ranked NUMBER(1) DEFAULT 1 NOT NULL,
        is_fixed_amount NUMBER(1) DEFAULT 1 NOT NULL,
        description CLOB,
        material_description CLOB,
        application_start_at DATE,
        application_end_at DATE,
        publicity_start_at DATE,
        publicity_end_at DATE,
        scheme_status VARCHAR2(30 CHAR) DEFAULT 'draft' NOT NULL,
        created_by_user_id NUMBER,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_AWARD_SCHEMES_SCOPE UNIQUE (club_id, award_scheme_id),
        CONSTRAINT CK_AWARD_SCHEMES_CATEGORY CHECK (award_category IN ('honor', 'scholarship', 'competition', 'service', 'other')),
        CONSTRAINT CK_AWARD_SCHEMES_RANKED CHECK (is_ranked IN (0, 1)),
        CONSTRAINT CK_AWARD_SCHEMES_FIXED_AMOUNT CHECK (is_fixed_amount IN (0, 1)),
        CONSTRAINT CK_AWARD_SCHEMES_STATUS CHECK (scheme_status IN ('draft', 'open', 'reviewing', 'publicizing', 'archived', 'closed'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_LEVELS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_LEVELS (
        award_level_id NUMBER DEFAULT SEQ_AWARD_LEVELS.NEXTVAL PRIMARY KEY,
        award_scheme_id NUMBER NOT NULL,
        level_name VARCHAR2(255 CHAR) NOT NULL,
        award_score NUMBER DEFAULT 0 NOT NULL,
        amount NUMBER,
        quota NUMBER,
        display_order NUMBER DEFAULT 0 NOT NULL,
        level_status VARCHAR2(30 CHAR) DEFAULT 'active' NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_AWARD_LEVELS_SCOPE UNIQUE (award_scheme_id, award_level_id),
        CONSTRAINT UQ_AWARD_LEVELS_NAME UNIQUE (award_scheme_id, level_name),
        CONSTRAINT CK_AWARD_LEVELS_SCORE CHECK (award_score BETWEEN 0 AND 100),
        CONSTRAINT CK_AWARD_LEVELS_AMOUNT CHECK (amount IS NULL OR amount >= 0),
        CONSTRAINT CK_AWARD_LEVELS_QUOTA CHECK (quota IS NULL OR quota >= 0),
        CONSTRAINT CK_AWARD_LEVELS_STATUS CHECK (level_status IN ('active', 'inactive'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_APPLICATIONS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_APPLICATIONS (
        award_application_id NUMBER DEFAULT SEQ_AWARD_APPLICATIONS.NEXTVAL PRIMARY KEY,
        club_id NUMBER NOT NULL,
        award_scheme_id NUMBER NOT NULL,
        award_level_id NUMBER NOT NULL,
        applicant_user_id NUMBER NOT NULL,
        recommender_user_id NUMBER,
        submitter_user_id NUMBER NOT NULL,
        application_type VARCHAR2(30 CHAR) DEFAULT 'self' NOT NULL,
        application_reason CLOB,
        material_url VARCHAR2(1000 CHAR),
        current_step VARCHAR2(30 CHAR) DEFAULT 'student_submit' NOT NULL,
        application_status VARCHAR2(30 CHAR) DEFAULT 'draft' NOT NULL,
        public_status VARCHAR2(30 CHAR) DEFAULT 'none' NOT NULL,
        review_round NUMBER DEFAULT 1 NOT NULL,
        final_award_score NUMBER,
        final_amount NUMBER,
        submitted_at DATE,
        approved_at DATE,
        publicized_at DATE,
        archived_at DATE,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
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
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_REVIEW_RECORDS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_REVIEW_RECORDS (
        review_id NUMBER DEFAULT SEQ_AWARD_REVIEW_RECORDS.NEXTVAL PRIMARY KEY,
        award_application_id NUMBER NOT NULL,
        review_round NUMBER DEFAULT 1 NOT NULL,
        review_step VARCHAR2(30 CHAR) NOT NULL,
        review_result VARCHAR2(30 CHAR) NOT NULL,
        reviewer_user_id NUMBER,
        review_comment CLOB,
        from_status VARCHAR2(30 CHAR),
        to_status VARCHAR2(30 CHAR),
        reviewed_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT CK_AWARD_REVIEWS_ROUND CHECK (review_round >= 1),
        CONSTRAINT CK_AWARD_REVIEWS_STEP CHECK (review_step IN ('student_submit', 'club_review', 'advisor_review', 'school_review', 'publicity', 'archive')),
        CONSTRAINT CK_AWARD_REVIEWS_RESULT CHECK (review_result IN ('submit', 'approve', 'reject', 'return', 'publish', 'archive', 'withdraw'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_ATTACHMENTS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_ATTACHMENTS (
        attachment_id NUMBER DEFAULT SEQ_AWARD_ATTACHMENTS.NEXTVAL PRIMARY KEY,
        award_application_id NUMBER NOT NULL,
        attachment_name VARCHAR2(255 CHAR) NOT NULL,
        attachment_url VARCHAR2(1000 CHAR) NOT NULL,
        attachment_type VARCHAR2(100 CHAR),
        uploaded_by_user_id NUMBER NOT NULL,
        uploaded_at DATE DEFAULT SYSDATE NOT NULL
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_PUBLICITY_BATCHES';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_PUBLICITY_BATCHES (
        publicity_batch_id NUMBER DEFAULT SEQ_AWARD_PUBLICITY_BATCHES.NEXTVAL PRIMARY KEY,
        club_id NUMBER NOT NULL,
        title VARCHAR2(255 CHAR) NOT NULL,
        description CLOB,
        publicity_start_at DATE,
        publicity_end_at DATE,
        publicity_status VARCHAR2(30 CHAR) DEFAULT 'draft' NOT NULL,
        publisher_user_id NUMBER,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_AWARD_PUBLICITY_BATCH_SCOPE UNIQUE (club_id, publicity_batch_id),
        CONSTRAINT CK_AWARD_PUBLICITY_STATUS CHECK (publicity_status IN ('draft', 'publicizing', 'closed', 'archived'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'AWARD_PUBLICITY_ITEMS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_PUBLICITY_ITEMS (
        publicity_item_id NUMBER DEFAULT SEQ_AWARD_PUBLICITY_ITEMS.NEXTVAL PRIMARY KEY,
        publicity_batch_id NUMBER NOT NULL,
        club_id NUMBER NOT NULL,
        award_application_id NUMBER NOT NULL,
        display_order NUMBER DEFAULT 0 NOT NULL,
        publicity_result VARCHAR2(30 CHAR) DEFAULT 'normal' NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_AWARD_PUBLICITY_ITEMS_APP UNIQUE (publicity_batch_id, award_application_id),
        CONSTRAINT CK_AWARD_PUBLICITY_ITEMS_RESULT CHECK (publicity_result IN ('normal', 'withdrawn', 'corrected'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'EVALUATION_AWARD_SOURCES';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE EVALUATION_AWARD_SOURCES (
        club_id NUMBER NOT NULL,
        user_id NUMBER NOT NULL,
        evaluation_id NUMBER NOT NULL,
        award_application_id NUMBER NOT NULL,
        award_score NUMBER DEFAULT 0 NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT PK_EVALUATION_AWARD_SOURCES PRIMARY KEY (evaluation_id, award_application_id),
        CONSTRAINT CK_EVALUATION_AWARD_SOURCES_SCORE CHECK (award_score BETWEEN 0 AND 100)
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  FOR item IN (
    SELECT 'UQ_AWARD_SCHEMES_NAME' AS index_name,
           'CREATE UNIQUE INDEX UQ_AWARD_SCHEMES_NAME ON AWARD_SCHEMES (club_id, award_name, academic_year, NVL(term_name, ''-''))' AS ddl FROM dual UNION ALL
    SELECT 'IX_AWARD_SCHEMES_CLUB_STATUS',
           'CREATE INDEX IX_AWARD_SCHEMES_CLUB_STATUS ON AWARD_SCHEMES (club_id, scheme_status, application_start_at)' FROM dual UNION ALL
    SELECT 'IX_AWARD_LEVELS_ORDER',
           'CREATE INDEX IX_AWARD_LEVELS_ORDER ON AWARD_LEVELS (award_scheme_id, display_order, level_name)' FROM dual UNION ALL
    SELECT 'IX_AWARD_APPLICATIONS_STATUS',
           'CREATE INDEX IX_AWARD_APPLICATIONS_STATUS ON AWARD_APPLICATIONS (club_id, application_status, current_step)' FROM dual UNION ALL
    SELECT 'IX_AWARD_APPLICATIONS_USER',
           'CREATE INDEX IX_AWARD_APPLICATIONS_USER ON AWARD_APPLICATIONS (applicant_user_id, club_id, award_scheme_id)' FROM dual UNION ALL
    SELECT 'IX_AWARD_REVIEWS_APPLICATION',
           'CREATE INDEX IX_AWARD_REVIEWS_APPLICATION ON AWARD_REVIEW_RECORDS (award_application_id, review_round, reviewed_at)' FROM dual UNION ALL
    SELECT 'IX_AWARD_ATTACHMENTS_APP',
           'CREATE INDEX IX_AWARD_ATTACHMENTS_APP ON AWARD_ATTACHMENTS (award_application_id, uploaded_at)' FROM dual UNION ALL
    SELECT 'IX_AWARD_PUBLICITY_CLUB',
           'CREATE INDEX IX_AWARD_PUBLICITY_CLUB ON AWARD_PUBLICITY_BATCHES (club_id, publicity_status, publicity_start_at)' FROM dual UNION ALL
    SELECT 'IX_AWARD_PUBLICITY_ITEMS_ORDER',
           'CREATE INDEX IX_AWARD_PUBLICITY_ITEMS_ORDER ON AWARD_PUBLICITY_ITEMS (publicity_batch_id, display_order)' FROM dual
  ) LOOP
    SELECT COUNT(*) INTO existing_count
    FROM user_indexes
    WHERE index_name = item.index_name;

    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE item.ddl;
    END IF;
  END LOOP;
END;
/

DECLARE
  existing_count NUMBER;

  PROCEDURE add_constraint(name_in VARCHAR2, ddl_in VARCHAR2) IS
  BEGIN
    SELECT COUNT(*) INTO existing_count
    FROM user_constraints
    WHERE constraint_name = name_in;

    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE ddl_in;
    END IF;
  END;
BEGIN
  add_constraint('UQ_EVALUATIONS_SOURCE_SCOPE',
    'ALTER TABLE EVALUATIONS ADD CONSTRAINT UQ_EVALUATIONS_SOURCE_SCOPE UNIQUE (club_id, user_id, evaluation_id)');
  add_constraint('UQ_AWARD_APPLICATIONS_MEMBER_SCOPE',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT UQ_AWARD_APPLICATIONS_MEMBER_SCOPE UNIQUE (club_id, applicant_user_id, award_application_id)');
  add_constraint('FK_AWARD_SCHEMES_CLUB',
    'ALTER TABLE AWARD_SCHEMES ADD CONSTRAINT FK_AWARD_SCHEMES_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_SCHEMES_CREATOR',
    'ALTER TABLE AWARD_SCHEMES ADD CONSTRAINT FK_AWARD_SCHEMES_CREATOR FOREIGN KEY (created_by_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_LEVELS_SCHEME',
    'ALTER TABLE AWARD_LEVELS ADD CONSTRAINT FK_AWARD_LEVELS_SCHEME FOREIGN KEY (award_scheme_id) REFERENCES AWARD_SCHEMES (award_scheme_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_APPLICATIONS_CLUB',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_APPLICATIONS_SCHEME',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_SCHEME FOREIGN KEY (club_id, award_scheme_id) REFERENCES AWARD_SCHEMES (club_id, award_scheme_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_APPLICATIONS_LEVEL',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_LEVEL FOREIGN KEY (award_scheme_id, award_level_id) REFERENCES AWARD_LEVELS (award_scheme_id, award_level_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_APPLICATIONS_APPLICANT',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_APPLICANT FOREIGN KEY (applicant_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_APPLICATIONS_RECOMMENDER',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_RECOMMENDER FOREIGN KEY (recommender_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_APPLICATIONS_SUBMITTER',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_SUBMITTER FOREIGN KEY (submitter_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_REVIEWS_APPLICATION',
    'ALTER TABLE AWARD_REVIEW_RECORDS ADD CONSTRAINT FK_AWARD_REVIEWS_APPLICATION FOREIGN KEY (award_application_id) REFERENCES AWARD_APPLICATIONS (award_application_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_REVIEWS_REVIEWER',
    'ALTER TABLE AWARD_REVIEW_RECORDS ADD CONSTRAINT FK_AWARD_REVIEWS_REVIEWER FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_ATTACHMENTS_APPLICATION',
    'ALTER TABLE AWARD_ATTACHMENTS ADD CONSTRAINT FK_AWARD_ATTACHMENTS_APPLICATION FOREIGN KEY (award_application_id) REFERENCES AWARD_APPLICATIONS (award_application_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_ATTACHMENTS_UPLOADER',
    'ALTER TABLE AWARD_ATTACHMENTS ADD CONSTRAINT FK_AWARD_ATTACHMENTS_UPLOADER FOREIGN KEY (uploaded_by_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_PUBLICITY_CLUB',
    'ALTER TABLE AWARD_PUBLICITY_BATCHES ADD CONSTRAINT FK_AWARD_PUBLICITY_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_PUBLICITY_PUBLISHER',
    'ALTER TABLE AWARD_PUBLICITY_BATCHES ADD CONSTRAINT FK_AWARD_PUBLICITY_PUBLISHER FOREIGN KEY (publisher_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_PUBLICITY_ITEMS_BATCH',
    'ALTER TABLE AWARD_PUBLICITY_ITEMS ADD CONSTRAINT FK_AWARD_PUBLICITY_ITEMS_BATCH FOREIGN KEY (club_id, publicity_batch_id) REFERENCES AWARD_PUBLICITY_BATCHES (club_id, publicity_batch_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_PUBLICITY_ITEMS_APP',
    'ALTER TABLE AWARD_PUBLICITY_ITEMS ADD CONSTRAINT FK_AWARD_PUBLICITY_ITEMS_APP FOREIGN KEY (club_id, award_application_id) REFERENCES AWARD_APPLICATIONS (club_id, award_application_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_EAS_EVALUATION',
    'ALTER TABLE EVALUATION_AWARD_SOURCES ADD CONSTRAINT FK_EAS_EVALUATION FOREIGN KEY (club_id, user_id, evaluation_id) REFERENCES EVALUATIONS (club_id, user_id, evaluation_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_EAS_APPLICATION',
    'ALTER TABLE EVALUATION_AWARD_SOURCES ADD CONSTRAINT FK_EAS_APPLICATION FOREIGN KEY (club_id, user_id, award_application_id) REFERENCES AWARD_APPLICATIONS (club_id, applicant_user_id, award_application_id) DEFERRABLE INITIALLY IMMEDIATE');
END;
/
