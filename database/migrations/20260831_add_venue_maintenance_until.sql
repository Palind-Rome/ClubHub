-- Issue #166: persist venue maintenance deadlines in Oracle.
--
-- Execution prerequisites:
--   1. Back up the target CLUBHUB schema.
--   2. Stop venue status writes during the migration.
--   3. Run as the CLUBHUB schema owner, then execute database/verify.sql.
--
-- Semantics:
--   - MAINTENANCE_UNTIL is nullable and stores UTC wall-clock time in Oracle DATE.
--   - A null value means that the maintenance end time is not set.
--   - The application clears the value when VENUE_STATUS leaves maintenance.
--
-- Rollback outline for a non-production test database:
--   ALTER TABLE VENUES DROP CONSTRAINT CK_VENUES_MAINTENANCE_UNTIL;
--   ALTER TABLE VENUES DROP COLUMN MAINTENANCE_UNTIL;
-- Oracle DDL commits implicitly; take a backup before attempting rollback.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;
SET DEFINE OFF;

DECLARE
  table_count NUMBER;
  column_count NUMBER;
  date_column_count NUMBER;
  constraint_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_count
  FROM user_tables
  WHERE table_name = 'VENUES';

  IF table_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20061, 'VENUES does not exist; run the base schema first.');
  END IF;

  SELECT COUNT(*) INTO column_count
  FROM user_tab_columns
  WHERE table_name = 'VENUES'
    AND column_name = 'MAINTENANCE_UNTIL';

  IF column_count = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE VENUES ADD (maintenance_until DATE)';
  ELSE
    SELECT COUNT(*) INTO date_column_count
    FROM user_tab_columns
    WHERE table_name = 'VENUES'
      AND column_name = 'MAINTENANCE_UNTIL'
      AND data_type = 'DATE';

    IF date_column_count = 0 THEN
      RAISE_APPLICATION_ERROR(-20062, 'VENUES.MAINTENANCE_UNTIL exists with an unexpected type.');
    END IF;
  END IF;

  SELECT COUNT(*) INTO constraint_count
  FROM user_constraints
  WHERE table_name = 'VENUES'
    AND constraint_name = 'CK_VENUES_MAINTENANCE_UNTIL';

  IF constraint_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      ALTER TABLE VENUES ADD CONSTRAINT CK_VENUES_MAINTENANCE_UNTIL CHECK (
        maintenance_until IS NULL OR
        NVL(LOWER(TRIM(venue_status)), '#') = 'maintenance'
      )
    ]';
  END IF;
END;
/

COMMIT;
