-- Issue #138: add the minimum budget-management closed loop.
--
-- Execution prerequisites:
--   1. Back up the target CLUBHUB schema.
--   2. Run as the CLUBHUB schema owner during a maintenance window.
--   3. Stop application writes that may submit or review activity budgets until
--      this migration and database/verify.sql complete.
--
-- Rollback outline for a non-production test database:
--   1. Drop FKs FK_BUDGET_TXN_CLUB, FK_BUDGET_TXN_APPLICATION,
--      FK_BUDGET_TXN_ACCOUNT, FK_BUDGET_REVIEWS_REVIEWER,
--      FK_BUDGET_REVIEWS_APPLICATION, FK_BUDGET_APPLICATIONS_REVIEWER,
--      FK_BUDGET_APPLICATIONS_APPLICANT, FK_BUDGET_APPLICATIONS_ACTIVITY,
--      FK_BUDGET_APPLICATIONS_CLUB, FK_BUDGET_APPLICATIONS_ACCOUNT,
--      FK_BUDGET_ACCOUNTS_CLUB.
--   2. Drop tables in dependency order: BUDGET_TRANSACTIONS,
--      BUDGET_REVIEW_RECORDS, BUDGET_APPLICATIONS, BUDGET_ACCOUNTS.
--   3. Drop SEQ_BUDGET_* sequences.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  existing_count NUMBER;
BEGIN
  FOR item IN (
    SELECT 'SEQ_BUDGET_ACCOUNTS' AS sequence_name FROM dual UNION ALL
    SELECT 'SEQ_BUDGET_APPLICATIONS' FROM dual UNION ALL
    SELECT 'SEQ_BUDGET_REVIEW_RECORDS' FROM dual UNION ALL
    SELECT 'SEQ_BUDGET_TRANSACTIONS' FROM dual
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
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'BUDGET_ACCOUNTS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE BUDGET_ACCOUNTS (
        account_id NUMBER DEFAULT SEQ_BUDGET_ACCOUNTS.NEXTVAL PRIMARY KEY,
        club_id NUMBER NOT NULL,
        fiscal_year VARCHAR2(20 CHAR) NOT NULL,
        account_name VARCHAR2(255 CHAR) NOT NULL,
        initial_amount NUMBER(12,2) DEFAULT 0 NOT NULL,
        account_status VARCHAR2(30 CHAR) DEFAULT 'active' NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_BUDGET_ACCOUNTS_SCOPE UNIQUE (club_id, account_id),
        CONSTRAINT UQ_BUDGET_ACCOUNTS_YEAR UNIQUE (club_id, fiscal_year),
        CONSTRAINT CK_BUDGET_ACCOUNTS_AMOUNT CHECK (initial_amount >= 0),
        CONSTRAINT CK_BUDGET_ACCOUNTS_STATUS CHECK (account_status IN ('active', 'closed'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'BUDGET_APPLICATIONS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE BUDGET_APPLICATIONS (
        application_id NUMBER DEFAULT SEQ_BUDGET_APPLICATIONS.NEXTVAL PRIMARY KEY,
        account_id NUMBER NOT NULL,
        club_id NUMBER NOT NULL,
        activity_id NUMBER,
        applicant_user_id NUMBER NOT NULL,
        application_type VARCHAR2(30 CHAR) NOT NULL,
        title VARCHAR2(255 CHAR) NOT NULL,
        amount NUMBER(12,2) NOT NULL,
        purpose VARCHAR2(255 CHAR) NOT NULL,
        detail CLOB,
        application_status VARCHAR2(30 CHAR) DEFAULT 'pending' NOT NULL,
        submitted_at DATE DEFAULT SYSDATE NOT NULL,
        reviewed_at DATE,
        reviewer_user_id NUMBER,
        review_comment VARCHAR2(255 CHAR),
        created_at DATE DEFAULT SYSDATE NOT NULL,
        updated_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_BUDGET_APPLICATIONS_SCOPE UNIQUE (club_id, application_id),
        CONSTRAINT UQ_BUDGET_APPLICATIONS_ACCOUNT_SCOPE UNIQUE (club_id, account_id, application_id),
        CONSTRAINT CK_BUDGET_APPLICATIONS_TYPE CHECK (application_type IN ('activity_budget', 'purchase', 'reimbursement')),
        CONSTRAINT CK_BUDGET_APPLICATIONS_AMOUNT CHECK (amount > 0),
        CONSTRAINT CK_BUDGET_APPLICATIONS_STATUS CHECK (application_status IN ('pending', 'approved', 'rejected', 'cancelled'))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'BUDGET_REVIEW_RECORDS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE BUDGET_REVIEW_RECORDS (
        review_id NUMBER DEFAULT SEQ_BUDGET_REVIEW_RECORDS.NEXTVAL PRIMARY KEY,
        application_id NUMBER NOT NULL,
        reviewer_user_id NUMBER NOT NULL,
        approved NUMBER(1) NOT NULL,
        comment_text VARCHAR2(255 CHAR),
        reviewed_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT CK_BUDGET_REVIEWS_APPROVED CHECK (approved IN (0, 1))
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count FROM user_tables WHERE table_name = 'BUDGET_TRANSACTIONS';
  IF existing_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE BUDGET_TRANSACTIONS (
        transaction_id NUMBER DEFAULT SEQ_BUDGET_TRANSACTIONS.NEXTVAL PRIMARY KEY,
        account_id NUMBER NOT NULL,
        application_id NUMBER,
        club_id NUMBER NOT NULL,
        transaction_type VARCHAR2(30 CHAR) NOT NULL,
        amount NUMBER(12,2) NOT NULL,
        description VARCHAR2(255 CHAR),
        occurred_at DATE DEFAULT SYSDATE NOT NULL,
        created_at DATE DEFAULT SYSDATE NOT NULL,
        CONSTRAINT UQ_BUDGET_TXN_APPLICATION UNIQUE (application_id, transaction_type),
        CONSTRAINT CK_BUDGET_TXN_TYPE CHECK (transaction_type IN ('commitment', 'expense', 'refund', 'adjustment')),
        CONSTRAINT CK_BUDGET_TXN_AMOUNT CHECK (amount <> 0)
      )
    ]';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  FOR item IN (
    SELECT 'IX_BUDGET_APPLICATIONS_CLUB' AS index_name,
           'CREATE INDEX IX_BUDGET_APPLICATIONS_CLUB ON BUDGET_APPLICATIONS (club_id, application_status, submitted_at)' AS ddl
    FROM dual UNION ALL
    SELECT 'IX_BUDGET_APPLICATIONS_ACCOUNT',
           'CREATE INDEX IX_BUDGET_APPLICATIONS_ACCOUNT ON BUDGET_APPLICATIONS (account_id, application_status)'
    FROM dual UNION ALL
    SELECT 'IX_BUDGET_TRANSACTIONS_ACCOUNT',
           'CREATE INDEX IX_BUDGET_TRANSACTIONS_ACCOUNT ON BUDGET_TRANSACTIONS (account_id, occurred_at)'
    FROM dual
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
BEGIN
  FOR item IN (
    SELECT 'UQ_BUDGET_APPLICATIONS_ACCOUNT_SCOPE' AS constraint_name,
           'BUDGET_APPLICATIONS' AS table_name,
           'ALTER TABLE BUDGET_APPLICATIONS ADD CONSTRAINT UQ_BUDGET_APPLICATIONS_ACCOUNT_SCOPE UNIQUE (club_id, account_id, application_id)' AS ddl
    FROM dual
  ) LOOP
    SELECT COUNT(*) INTO existing_count
    FROM user_constraints
    WHERE table_name = item.table_name
      AND constraint_name = item.constraint_name;

    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE item.ddl;
    END IF;
  END LOOP;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO existing_count
  FROM user_constraints
  WHERE table_name = 'BUDGET_TRANSACTIONS'
    AND constraint_name = 'FK_BUDGET_TXN_APPLICATION';

  IF existing_count > 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE BUDGET_TRANSACTIONS DROP CONSTRAINT FK_BUDGET_TXN_APPLICATION';
  END IF;
END;
/

DECLARE
  existing_count NUMBER;
BEGIN
  FOR item IN (
    SELECT 'FK_BUDGET_ACCOUNTS_CLUB' AS constraint_name,
           'BUDGET_ACCOUNTS' AS table_name,
           'ALTER TABLE BUDGET_ACCOUNTS ADD CONSTRAINT FK_BUDGET_ACCOUNTS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE' AS ddl
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_APPLICATIONS_ACCOUNT', 'BUDGET_APPLICATIONS',
           'ALTER TABLE BUDGET_APPLICATIONS ADD CONSTRAINT FK_BUDGET_APPLICATIONS_ACCOUNT FOREIGN KEY (club_id, account_id) REFERENCES BUDGET_ACCOUNTS (club_id, account_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_APPLICATIONS_CLUB', 'BUDGET_APPLICATIONS',
           'ALTER TABLE BUDGET_APPLICATIONS ADD CONSTRAINT FK_BUDGET_APPLICATIONS_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_APPLICATIONS_ACTIVITY', 'BUDGET_APPLICATIONS',
           'ALTER TABLE BUDGET_APPLICATIONS ADD CONSTRAINT FK_BUDGET_APPLICATIONS_ACTIVITY FOREIGN KEY (activity_id) REFERENCES ACTIVITIES (activity_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_APPLICATIONS_APPLICANT', 'BUDGET_APPLICATIONS',
           'ALTER TABLE BUDGET_APPLICATIONS ADD CONSTRAINT FK_BUDGET_APPLICATIONS_APPLICANT FOREIGN KEY (applicant_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_APPLICATIONS_REVIEWER', 'BUDGET_APPLICATIONS',
           'ALTER TABLE BUDGET_APPLICATIONS ADD CONSTRAINT FK_BUDGET_APPLICATIONS_REVIEWER FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_REVIEWS_APPLICATION', 'BUDGET_REVIEW_RECORDS',
           'ALTER TABLE BUDGET_REVIEW_RECORDS ADD CONSTRAINT FK_BUDGET_REVIEWS_APPLICATION FOREIGN KEY (application_id) REFERENCES BUDGET_APPLICATIONS (application_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_REVIEWS_REVIEWER', 'BUDGET_REVIEW_RECORDS',
           'ALTER TABLE BUDGET_REVIEW_RECORDS ADD CONSTRAINT FK_BUDGET_REVIEWS_REVIEWER FOREIGN KEY (reviewer_user_id) REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_TXN_ACCOUNT', 'BUDGET_TRANSACTIONS',
           'ALTER TABLE BUDGET_TRANSACTIONS ADD CONSTRAINT FK_BUDGET_TXN_ACCOUNT FOREIGN KEY (club_id, account_id) REFERENCES BUDGET_ACCOUNTS (club_id, account_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_TXN_APPLICATION', 'BUDGET_TRANSACTIONS',
           'ALTER TABLE BUDGET_TRANSACTIONS ADD CONSTRAINT FK_BUDGET_TXN_APPLICATION FOREIGN KEY (club_id, account_id, application_id) REFERENCES BUDGET_APPLICATIONS (club_id, account_id, application_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual UNION ALL
    SELECT 'FK_BUDGET_TXN_CLUB', 'BUDGET_TRANSACTIONS',
           'ALTER TABLE BUDGET_TRANSACTIONS ADD CONSTRAINT FK_BUDGET_TXN_CLUB FOREIGN KEY (club_id) REFERENCES CLUBS (club_id) DEFERRABLE INITIALLY IMMEDIATE'
    FROM dual
  ) LOOP
    SELECT COUNT(*) INTO existing_count
    FROM user_constraints
    WHERE table_name = item.table_name
      AND constraint_name = item.constraint_name;

    IF existing_count = 0 THEN
      EXECUTE IMMEDIATE item.ddl;
    END IF;
  END LOOP;
END;
/

COMMIT;
