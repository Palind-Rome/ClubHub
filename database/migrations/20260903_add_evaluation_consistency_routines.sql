-- Issue #217: keep member evaluation results consistent and publish a term atomically.
--
-- Execution prerequisites:
--   1. Back up the target CLUBHUB schema.
--   2. Pause evaluation generation, editing and publication writes.
--   3. Run as the CLUBHUB schema owner during a maintenance window.
--
-- Impact scope:
--   * Existing semester EVALUATIONS rows are normalized: null component scores
--     become 0, total_score is recalculated and grade is derived from the
--     current rule. Award evaluation records keep their award-specific meaning.
--   * FN_EVALUATION_GRADE provides the shared score-to-grade rule.
--   * TRG_EVALUATIONS_DERIVE_SCORE rejects out-of-range component scores and
--     derives component defaults, total_score and grade for future semester writes.
--   * SP_PUBLISH_TERM_EVALUATIONS publishes one club and term in one statement.
--   * No tables, columns, constraints or indexes are added or removed.
--
-- The procedure does not commit. The caller remains responsible for application
-- authorization and for committing or rolling back the surrounding transaction.
-- CREATE OR REPLACE is used so the script can be rerun after a review fix.
-- Oracle DDL commits implicitly.
--
-- Rollback (run as the schema owner after stopping evaluation writes):
--   DROP TRIGGER TRG_EVALUATIONS_DERIVE_SCORE;
--   DROP PROCEDURE SP_PUBLISH_TERM_EVALUATIONS;
--   DROP FUNCTION FN_EVALUATION_GRADE;
--   Dropping the objects restores application-only calculation and publication.
--   Historical normalization is a data correction and must be restored from the
--   pre-migration backup if it needs to be reversed.

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  l_invalid_score_count PLS_INTEGER;
BEGIN
  SELECT COUNT(*)
    INTO l_invalid_score_count
    FROM evaluations
   WHERE LOWER(TRIM(NVL(evaluation_type, '#'))) = 'semester'
     AND (
            NVL(activity_score, 0) NOT BETWEEN 0 AND 100
      OR NVL(task_score, 0) NOT BETWEEN 0 AND 100
      OR NVL(learning_score, 0) NOT BETWEEN 0 AND 100
      OR NVL(award_score, 0) NOT BETWEEN 0 AND 100
         );

  IF l_invalid_score_count > 0 THEN
    RAISE_APPLICATION_ERROR(
      -20160,
      '存在超出 0 至 100 范围的考核分项，请先完成数据核查。'
    );
  END IF;
END;
/

CREATE OR REPLACE FUNCTION FN_EVALUATION_GRADE (
  p_total_score IN NUMBER
) RETURN VARCHAR2
DETERMINISTIC
AUTHID DEFINER
IS
  l_total_score NUMBER := NVL(p_total_score, 0);
BEGIN
  IF l_total_score < 0 OR l_total_score > 400 THEN
    RAISE_APPLICATION_ERROR(-20161, '考核总分必须处于 0 至 400 分范围内。');
  END IF;

  IF l_total_score >= 320 THEN
    RETURN '优秀';
  ELSIF l_total_score >= 260 THEN
    RETURN '良好';
  ELSIF l_total_score >= 200 THEN
    RETURN '合格';
  ELSE
    RETURN '待提升';
  END IF;
END FN_EVALUATION_GRADE;
/

UPDATE evaluations
   SET activity_score = NVL(activity_score, 0),
       task_score = NVL(task_score, 0),
       learning_score = NVL(learning_score, 0),
       award_score = NVL(award_score, 0),
       total_score = NVL(activity_score, 0)
                   + NVL(task_score, 0)
                   + NVL(learning_score, 0)
                   + NVL(award_score, 0),
       grade = FN_EVALUATION_GRADE(
         NVL(activity_score, 0)
         + NVL(task_score, 0)
         + NVL(learning_score, 0)
         + NVL(award_score, 0)
       )
 WHERE LOWER(TRIM(NVL(evaluation_type, '#'))) = 'semester'
   AND (
         activity_score IS NULL
      OR task_score IS NULL
      OR learning_score IS NULL
      OR award_score IS NULL
      OR NVL(total_score, -1) <> NVL(activity_score, 0)
                                      + NVL(task_score, 0)
                                      + NVL(learning_score, 0)
                                      + NVL(award_score, 0)
      OR NVL(TRIM(grade), '#') <> FN_EVALUATION_GRADE(
           NVL(activity_score, 0)
           + NVL(task_score, 0)
           + NVL(learning_score, 0)
           + NVL(award_score, 0)
         )
       );

CREATE OR REPLACE TRIGGER TRG_EVALUATIONS_DERIVE_SCORE
BEFORE INSERT OR UPDATE ON EVALUATIONS
FOR EACH ROW
DECLARE
  l_activity_score EVALUATIONS.ACTIVITY_SCORE%TYPE;
  l_task_score     EVALUATIONS.TASK_SCORE%TYPE;
  l_learning_score EVALUATIONS.LEARNING_SCORE%TYPE;
  l_award_score    EVALUATIONS.AWARD_SCORE%TYPE;
BEGIN
  IF LOWER(TRIM(NVL(:NEW.EVALUATION_TYPE, '#'))) = 'semester' THEN
    l_activity_score := NVL(:NEW.ACTIVITY_SCORE, 0);
    l_task_score := NVL(:NEW.TASK_SCORE, 0);
    l_learning_score := NVL(:NEW.LEARNING_SCORE, 0);
    l_award_score := NVL(:NEW.AWARD_SCORE, 0);

    IF l_activity_score NOT BETWEEN 0 AND 100 THEN
      RAISE_APPLICATION_ERROR(-20162, '活动表现分必须处于 0 至 100 分范围内。');
    END IF;

    IF l_task_score NOT BETWEEN 0 AND 100 THEN
      RAISE_APPLICATION_ERROR(-20163, '任务贡献分必须处于 0 至 100 分范围内。');
    END IF;

    IF l_learning_score NOT BETWEEN 0 AND 100 THEN
      RAISE_APPLICATION_ERROR(-20164, '学习成长分必须处于 0 至 100 分范围内。');
    END IF;

    IF l_award_score NOT BETWEEN 0 AND 100 THEN
      RAISE_APPLICATION_ERROR(-20165, '获奖加分必须处于 0 至 100 分范围内。');
    END IF;

    :NEW.ACTIVITY_SCORE := l_activity_score;
    :NEW.TASK_SCORE := l_task_score;
    :NEW.LEARNING_SCORE := l_learning_score;
    :NEW.AWARD_SCORE := l_award_score;
    :NEW.TOTAL_SCORE := l_activity_score
                      + l_task_score
                      + l_learning_score
                      + l_award_score;
    :NEW.GRADE := FN_EVALUATION_GRADE(:NEW.TOTAL_SCORE);
  END IF;
END TRG_EVALUATIONS_DERIVE_SCORE;
/

CREATE OR REPLACE PROCEDURE SP_PUBLISH_TERM_EVALUATIONS (
  p_club_id          IN EVALUATIONS.CLUB_ID%TYPE,
  p_term_name        IN EVALUATIONS.TERM_NAME%TYPE,
  p_evaluator_user_id IN EVALUATIONS.EVALUATOR_USER_ID%TYPE
)
AUTHID DEFINER
IS
  l_locked_club_id CLUBS.CLUB_ID%TYPE;
  l_evaluator_count PLS_INTEGER;
BEGIN
  IF p_club_id IS NULL OR p_club_id <= 0 THEN
    RAISE_APPLICATION_ERROR(-20166, '社团编号必须为正数。');
  END IF;

  IF p_term_name IS NULL OR LENGTH(TRIM(p_term_name)) = 0 THEN
    RAISE_APPLICATION_ERROR(-20167, '考核学期不能为空。');
  END IF;

  IF p_evaluator_user_id IS NULL OR p_evaluator_user_id <= 0 THEN
    RAISE_APPLICATION_ERROR(-20168, '考核人编号必须为正数。');
  END IF;

  BEGIN
    SELECT club_id
      INTO l_locked_club_id
      FROM clubs
     WHERE club_id = p_club_id
     FOR UPDATE;
  EXCEPTION
    WHEN NO_DATA_FOUND THEN
      RAISE_APPLICATION_ERROR(-20169, '待公示考核所属社团不存在。');
  END;

  SELECT COUNT(*)
    INTO l_evaluator_count
    FROM users
   WHERE user_id = p_evaluator_user_id;

  IF l_evaluator_count = 0 THEN
    RAISE_APPLICATION_ERROR(-20170, '考核人不存在。');
  END IF;

  UPDATE evaluations
     SET public_status = 'published',
         evaluator_user_id = p_evaluator_user_id
   WHERE club_id = p_club_id
     AND TRIM(term_name) = TRIM(p_term_name)
     AND LOWER(TRIM(NVL(evaluation_type, '#'))) = 'semester'
     AND LOWER(TRIM(NVL(public_status, '#'))) = 'draft';

  IF SQL%ROWCOUNT = 0 THEN
    RAISE_APPLICATION_ERROR(-20171, '指定社团和学期没有可公示的草稿考核。');
  END IF;
END SP_PUBLISH_TERM_EVALUATIONS;
/
