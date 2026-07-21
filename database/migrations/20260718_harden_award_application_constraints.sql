-- Issue #137: harden award application foreign keys for existing databases.
-- Execute only after backing up an existing development or test database.
-- This migration fixes legacy databases where an older same-named foreign key
-- may have referenced only award_scheme_id and therefore failed to protect
-- AWARD_APPLICATIONS.club_id from cross-club award references.
-- Manual rollback guide:
-- 1. Drop FK_AWARD_APPLICATIONS_LEVEL and FK_AWARD_APPLICATIONS_SCHEME.
-- 2. Recreate the previous constraints only if the previous structure is known
--    and intentionally accepted. The current expected structure is documented
--    in schema.sql and verify.sql.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET DEFINE OFF

DECLARE
  invalid_count NUMBER;
BEGIN
  SELECT COUNT(*) INTO invalid_count
  FROM award_applications application
  WHERE NOT EXISTS (
    SELECT 1
    FROM award_schemes scheme
    WHERE scheme.club_id = application.club_id
      AND scheme.award_scheme_id = application.award_scheme_id
  );

  IF invalid_count > 0 THEN
    RAISE_APPLICATION_ERROR(
      -20137,
      'AWARD_APPLICATIONS contains cross-club or missing award_scheme_id references; clean data before hardening FK_AWARD_APPLICATIONS_SCHEME.');
  END IF;

  SELECT COUNT(*) INTO invalid_count
  FROM award_applications application
  WHERE NOT EXISTS (
    SELECT 1
    FROM award_levels award_level
    WHERE award_level.award_scheme_id = application.award_scheme_id
      AND award_level.award_level_id = application.award_level_id
  );

  IF invalid_count > 0 THEN
    RAISE_APPLICATION_ERROR(
      -20138,
      'AWARD_APPLICATIONS contains missing or mismatched award_level_id references; clean data before hardening FK_AWARD_APPLICATIONS_LEVEL.');
  END IF;
END;
/

DECLARE
  PROCEDURE ensure_fk_columns(
    p_constraint_name IN VARCHAR2,
    p_table_name IN VARCHAR2,
    p_expected_columns IN VARCHAR2,
    p_create_sql IN VARCHAR2
  ) IS
    constraint_count NUMBER;
    actual_columns VARCHAR2(4000);
  BEGIN
    SELECT COUNT(*) INTO constraint_count
    FROM user_constraints
    WHERE constraint_name = p_constraint_name;

    IF constraint_count > 0 THEN
      SELECT LISTAGG(LOWER(column_name), ',') WITHIN GROUP (ORDER BY position)
      INTO actual_columns
      FROM user_cons_columns
      WHERE constraint_name = p_constraint_name;

      IF actual_columns <> p_expected_columns THEN
        EXECUTE IMMEDIATE 'ALTER TABLE ' || p_table_name || ' DROP CONSTRAINT ' || p_constraint_name;
      END IF;
    END IF;

    SELECT COUNT(*) INTO constraint_count
    FROM user_constraints
    WHERE constraint_name = p_constraint_name;

    IF constraint_count = 0 THEN
      EXECUTE IMMEDIATE p_create_sql;
    ELSE
      EXECUTE IMMEDIATE 'ALTER TABLE ' || p_table_name || ' MODIFY CONSTRAINT ' || p_constraint_name || ' ENABLE';
    END IF;
  END;
BEGIN
  ensure_fk_columns(
    'FK_AWARD_APPLICATIONS_SCHEME',
    'AWARD_APPLICATIONS',
    'club_id,award_scheme_id',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_SCHEME FOREIGN KEY (club_id, award_scheme_id) REFERENCES AWARD_SCHEMES (club_id, award_scheme_id) DEFERRABLE INITIALLY IMMEDIATE');

  ensure_fk_columns(
    'FK_AWARD_APPLICATIONS_LEVEL',
    'AWARD_APPLICATIONS',
    'award_scheme_id,award_level_id',
    'ALTER TABLE AWARD_APPLICATIONS ADD CONSTRAINT FK_AWARD_APPLICATIONS_LEVEL FOREIGN KEY (award_scheme_id, award_level_id) REFERENCES AWARD_LEVELS (award_scheme_id, award_level_id) DEFERRABLE INITIALLY IMMEDIATE');
END;
/

COMMIT;
