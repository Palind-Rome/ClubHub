-- Add award rule document table for scholarship / award evaluation criteria.
--
-- Rollback outline for a non-production test database:
-- 1. Drop FKs: FK_AWARD_RULE_DOCS_PUBLISHER, FK_AWARD_RULE_DOCS_CLUB.
-- 2. Drop table AWARD_RULE_DOCUMENTS.
-- 3. Drop sequence SEQ_AWARD_RULE_DOCUMENTS.

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count
  FROM user_sequences
  WHERE sequence_name = 'SEQ_AWARD_RULE_DOCUMENTS';

  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE SEQUENCE SEQ_AWARD_RULE_DOCUMENTS START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count
  FROM user_tables
  WHERE table_name = 'AWARD_RULE_DOCUMENTS';

  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE AWARD_RULE_DOCUMENTS (
        rule_document_id NUMBER DEFAULT SEQ_AWARD_RULE_DOCUMENTS.NEXTVAL PRIMARY KEY,
        club_id NUMBER,
        rule_title VARCHAR2(255 CHAR) NOT NULL,
        rule_scope VARCHAR2(30 CHAR) DEFAULT 'club' NOT NULL,
        academic_year VARCHAR2(50 CHAR) NOT NULL,
        term_name VARCHAR2(80 CHAR),
        issuer_name VARCHAR2(255 CHAR),
        summary CLOB,
        content_text CLOB,
        material_url VARCHAR2(1000 CHAR),
        material_name VARCHAR2(255 CHAR),
        version_no VARCHAR2(50 CHAR) DEFAULT '1.0' NOT NULL,
        rule_status VARCHAR2(30 CHAR) DEFAULT 'draft' NOT NULL,
        effective_start_at DATE,
        effective_end_at DATE,
        published_by_user_id NUMBER,
        published_at DATE,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT CK_AWARD_RULE_DOCS_SCOPE CHECK (rule_scope IN ('global', 'club')),
        CONSTRAINT CK_AWARD_RULE_DOCS_STATUS CHECK (rule_status IN ('draft', 'published', 'archived')),
        CONSTRAINT CK_AWARD_RULE_DOCS_CLUB CHECK (
          (rule_scope = 'global' AND club_id IS NULL) OR
          (rule_scope = 'club' AND club_id IS NOT NULL)
        )
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  FOR item IN (
    SELECT 'IX_AWARD_RULE_DOCS_SCOPE' AS index_name,
           'CREATE INDEX IX_AWARD_RULE_DOCS_SCOPE ON AWARD_RULE_DOCUMENTS (club_id, rule_status, academic_year, term_name)' AS ddl FROM dual UNION ALL
    SELECT 'IX_AWARD_RULE_DOCS_STATUS',
           'CREATE INDEX IX_AWARD_RULE_DOCS_STATUS ON AWARD_RULE_DOCUMENTS (rule_scope, rule_status, effective_start_at)' FROM dual
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
  add_constraint('FK_AWARD_RULE_DOCS_CLUB',
    'ALTER TABLE AWARD_RULE_DOCUMENTS ADD CONSTRAINT FK_AWARD_RULE_DOCS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE');
  add_constraint('FK_AWARD_RULE_DOCS_PUBLISHER',
    'ALTER TABLE AWARD_RULE_DOCUMENTS ADD CONSTRAINT FK_AWARD_RULE_DOCS_PUBLISHER FOREIGN KEY (published_by_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE');
END;
/
