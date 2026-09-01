-- Issue #215: add Oracle routines for budget and venue invariants.
--
-- Execution prerequisites:
--   1. Back up the target CLUBHUB schema.
--   2. Run as the CLUBHUB schema owner during a maintenance window.
--   3. Keep the existing application-level budget and venue checks enabled.
--
-- This migration creates no tables or columns. CREATE OR REPLACE is used so the
-- script can be rerun after a review fix. Oracle DDL commits implicitly.
--
-- Impact scope:
--   * FN_BUDGET_AVAILABLE_AMOUNT reads budget account and transaction rows.
--   * SP_REVIEW_BUDGET_APPLICATION updates a pending application and its review
--     record; an approval also writes one commitment transaction.
--   * TRG_VENUE_RESERVATION_OVERLAP checks INSERT/UPDATE statements that affect
--     approved venue reservations and may raise ORA-20052/20053/20054.
--   * IX_VENUE_RESERVATIONS_VENUE_ID adds an index for the trigger's venue scan.
--   * Existing business rows are not rewritten by this migration.
--
-- Preflight:
--   Run the approved-reservation interval checks in database/verify.sql before
--   deployment and record any historical anomalies for separate data cleanup.
--
-- Rollback (run as the schema owner after stopping related writes):
--   DROP INDEX IX_VENUE_RESERVATIONS_VENUE_ID;
--   DROP TRIGGER TRG_VENUE_RESERVATION_OVERLAP;
--   DROP PROCEDURE SP_REVIEW_BUDGET_APPLICATION;
--   DROP FUNCTION FN_BUDGET_AVAILABLE_AMOUNT;
--   Dropping these objects restores the previous application-only venue
--   reservation behavior. Restore backed-up definitions instead when the
--   target schema already had objects with these names.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  l_index_count PLS_INTEGER;
BEGIN
  SELECT COUNT(*)
    INTO l_index_count
    FROM user_indexes
   WHERE index_name = 'IX_VENUE_RESERVATIONS_VENUE_ID';

  IF l_index_count = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IX_VENUE_RESERVATIONS_VENUE_ID ON VENUE_RESERVATIONS (VENUE_ID)';
  END IF;
END;
/

CREATE OR REPLACE FUNCTION FN_BUDGET_AVAILABLE_AMOUNT (
  p_account_id IN BUDGET_ACCOUNTS.ACCOUNT_ID%TYPE
) RETURN NUMBER
AUTHID DEFINER
IS
  l_initial_amount BUDGET_ACCOUNTS.INITIAL_AMOUNT%TYPE;
  l_transaction_amount NUMBER;
BEGIN
  IF p_account_id IS NULL OR p_account_id <= 0 THEN
    RAISE_APPLICATION_ERROR(-20040, '经费账户编号必须为正数。');
  END IF;

  SELECT initial_amount
    INTO l_initial_amount
    FROM budget_accounts
   WHERE account_id = p_account_id;

  SELECT NVL(SUM(amount), 0)
    INTO l_transaction_amount
    FROM budget_transactions
   WHERE account_id = p_account_id;

  RETURN l_initial_amount + l_transaction_amount;
EXCEPTION
  WHEN NO_DATA_FOUND THEN
    RAISE_APPLICATION_ERROR(-20041, '经费账户不存在。');
END FN_BUDGET_AVAILABLE_AMOUNT;
/

CREATE OR REPLACE PROCEDURE SP_REVIEW_BUDGET_APPLICATION (
  p_application_id   IN BUDGET_APPLICATIONS.APPLICATION_ID%TYPE,
  p_reviewer_user_id IN BUDGET_APPLICATIONS.REVIEWER_USER_ID%TYPE,
  p_approved         IN NUMBER,
  p_comment          IN BUDGET_APPLICATIONS.REVIEW_COMMENT%TYPE
)
AUTHID DEFINER
IS
  l_application_status BUDGET_APPLICATIONS.APPLICATION_STATUS%TYPE;
  l_account_id         BUDGET_APPLICATIONS.ACCOUNT_ID%TYPE;
  l_club_id            BUDGET_APPLICATIONS.CLUB_ID%TYPE;
  l_amount             BUDGET_APPLICATIONS.AMOUNT%TYPE;
  l_title              BUDGET_APPLICATIONS.TITLE%TYPE;
  l_account_status     BUDGET_ACCOUNTS.ACCOUNT_STATUS%TYPE;
  l_available_amount   NUMBER;
  l_user_count         PLS_INTEGER;
BEGIN
  IF p_application_id IS NULL OR p_application_id <= 0 THEN
    RAISE_APPLICATION_ERROR(-20042, '经费申请编号必须为正数。');
  END IF;

  IF p_reviewer_user_id IS NULL OR p_reviewer_user_id <= 0 THEN
    RAISE_APPLICATION_ERROR(-20043, '审核人编号必须为正数。');
  END IF;

  IF p_approved IS NULL OR p_approved NOT IN (0, 1) THEN
    RAISE_APPLICATION_ERROR(-20044, '审核结果必须为 0 或 1。');
  END IF;

  IF p_comment IS NOT NULL AND LENGTH(p_comment) > 255 THEN
    RAISE_APPLICATION_ERROR(-20045, '审批意见不能超过 255 个字符。');
  END IF;

  SELECT application_status, account_id, club_id, amount, title
    INTO l_application_status, l_account_id, l_club_id, l_amount, l_title
    FROM budget_applications
   WHERE application_id = p_application_id
   FOR UPDATE;

  IF LOWER(TRIM(l_application_status)) <> 'pending' THEN
    RAISE_APPLICATION_ERROR(-20046, '只有待审核的经费申请才能审核。');
  END IF;

  SELECT COUNT(*)
    INTO l_user_count
    FROM users
   WHERE user_id = p_reviewer_user_id;

  IF l_user_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20047, '审核人不存在。');
  END IF;

  IF p_approved = 1 THEN
    SELECT account_status
      INTO l_account_status
      FROM budget_accounts
     WHERE account_id = l_account_id
       AND club_id = l_club_id
     FOR UPDATE;

    IF LOWER(TRIM(l_account_status)) <> 'active' THEN
      RAISE_APPLICATION_ERROR(-20048, '经费账户已关闭，不能审批通过新的经费申请。');
    END IF;

    l_available_amount := FN_BUDGET_AVAILABLE_AMOUNT(l_account_id);
    IF l_amount > l_available_amount THEN
      RAISE_APPLICATION_ERROR(-20049, '经费账户余额不足，不能审批通过该申请。');
    END IF;
  END IF;

  UPDATE budget_applications
     SET application_status = CASE WHEN p_approved = 1 THEN 'approved' ELSE 'rejected' END,
         reviewer_user_id = p_reviewer_user_id,
         review_comment = p_comment,
         reviewed_at = SYSDATE,
         updated_at = SYSDATE
   WHERE application_id = p_application_id;

  INSERT INTO budget_review_records (
    application_id,
    reviewer_user_id,
    approved,
    comment_text,
    reviewed_at
  ) VALUES (
    p_application_id,
    p_reviewer_user_id,
    p_approved,
    p_comment,
    SYSDATE
  );

  IF p_approved = 1 THEN
    INSERT INTO budget_transactions (
      account_id,
      application_id,
      club_id,
      transaction_type,
      amount,
      description,
      occurred_at,
      created_at
    ) VALUES (
      l_account_id,
      p_application_id,
      l_club_id,
      'commitment',
      -l_amount,
      '经费申请通过：' || l_title,
      SYSDATE,
      SYSDATE
    );
  END IF;
EXCEPTION
  WHEN NO_DATA_FOUND THEN
    RAISE_APPLICATION_ERROR(-20050, '经费申请或对应账户不存在。');
  WHEN DUP_VAL_ON_INDEX THEN
    RAISE_APPLICATION_ERROR(-20051, '该经费申请已经生成占用流水。');
END SP_REVIEW_BUDGET_APPLICATION;
/

CREATE OR REPLACE TRIGGER TRG_VENUE_RESERVATION_OVERLAP
FOR INSERT OR UPDATE OF VENUE_ID, START_AT, END_AT, RESERVATION_STATUS
ON VENUE_RESERVATIONS
COMPOUND TRIGGER
  TYPE venue_id_set IS TABLE OF BOOLEAN INDEX BY PLS_INTEGER;
  TYPE reservation_id_set IS TABLE OF BOOLEAN INDEX BY PLS_INTEGER;
  g_venue_ids venue_id_set;
  g_changed_ids reservation_id_set;

  AFTER EACH ROW IS
  BEGIN
    IF :NEW.VENUE_ID IS NOT NULL
       AND UPPER(TRIM(NVL(:NEW.RESERVATION_STATUS, '#'))) = 'APPROVED' THEN
      g_venue_ids(:NEW.VENUE_ID) := TRUE;
    END IF;

    IF UPDATING AND :OLD.VENUE_ID IS NOT NULL
       AND UPPER(TRIM(NVL(:OLD.RESERVATION_STATUS, '#'))) = 'APPROVED' THEN
      g_venue_ids(:OLD.VENUE_ID) := TRUE;
    END IF;

    IF :NEW.RESERVATION_ID IS NOT NULL THEN
      g_changed_ids(:NEW.RESERVATION_ID) := TRUE;
    END IF;

    IF UPDATING AND :OLD.RESERVATION_ID IS NOT NULL THEN
      g_changed_ids(:OLD.RESERVATION_ID) := TRUE;
    END IF;
  END AFTER EACH ROW;

  AFTER STATEMENT IS
    l_venue_id          PLS_INTEGER;
    l_reservation_id    PLS_INTEGER;
    l_locked_venue_id   VENUES.VENUE_ID%TYPE;
    l_invalid_count     PLS_INTEGER;
    l_conflict_count    PLS_INTEGER;
  BEGIN
    l_venue_id := g_venue_ids.FIRST;
    WHILE l_venue_id IS NOT NULL LOOP
      -- Lock the parent row in ascending venue order. This serializes approvals
      -- for the same venue without querying the mutating child table per row.
      BEGIN
        SELECT venue_id
          INTO l_locked_venue_id
          FROM venues
         WHERE venue_id = l_venue_id
         FOR UPDATE;
      EXCEPTION
        WHEN NO_DATA_FOUND THEN
          RAISE_APPLICATION_ERROR(-20052, '预约引用的场地不存在。');
      END;

      l_venue_id := g_venue_ids.NEXT(l_venue_id);
    END LOOP;

    -- Only rows changed by this statement participate in the validation. This
    -- keeps historical APPROVED anomalies from blocking unrelated approvals,
    -- while still checking every new or updated row against current data.
    l_reservation_id := g_changed_ids.FIRST;
    WHILE l_reservation_id IS NOT NULL LOOP
      SELECT COUNT(*)
        INTO l_invalid_count
        FROM venue_reservations reservation
       WHERE reservation.reservation_id = l_reservation_id
         AND UPPER(TRIM(NVL(reservation.reservation_status, '#'))) = 'APPROVED'
         AND (reservation.start_at IS NULL
              OR reservation.end_at IS NULL
              OR reservation.start_at >= reservation.end_at);

      IF l_invalid_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20053, '已通过预约的时间区间无效。');
      END IF;

      SELECT COUNT(*)
        INTO l_conflict_count
        FROM venue_reservations left_reservation
        JOIN venue_reservations right_reservation
          ON right_reservation.venue_id = left_reservation.venue_id
         AND right_reservation.reservation_id > left_reservation.reservation_id
       WHERE (left_reservation.reservation_id = l_reservation_id
              OR right_reservation.reservation_id = l_reservation_id)
         AND UPPER(TRIM(NVL(left_reservation.reservation_status, '#'))) = 'APPROVED'
         AND UPPER(TRIM(NVL(right_reservation.reservation_status, '#'))) = 'APPROVED'
         AND left_reservation.start_at < right_reservation.end_at
         AND right_reservation.start_at < left_reservation.end_at;

      IF l_conflict_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20054, '同一场地的已通过预约时间区间不能重叠。');
      END IF;

      l_reservation_id := g_changed_ids.NEXT(l_reservation_id);
    END LOOP;
  END AFTER STATEMENT;
END TRG_VENUE_RESERVATION_OVERLAP;
/
