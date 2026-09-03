using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace ClubHub.Api.OracleIntegrationTests;

public sealed class EvaluationRoutineOracleTests
{
    private const int MemberOneId = 9217001;
    private const int MemberTwoId = 9217002;
    private const int EvaluatorId = 9217003;
    private const int ClubId = 9217001;
    private const int FirstDraftId = 9217001;
    private const int SecondDraftId = 9217002;
    private const int OtherTermId = 9217003;
    private const int AwardEvaluationId = 9217004;
    private const int PublishedId = 9217005;
    private const int InvalidScoreId = 9217006;

    [OracleIntegrationFact]
    public async Task EvaluationRoutines_DeriveScoresAndPublishOnlyTheRequestedTerm()
    {
        await using var connection = new OracleConnection(OracleIntegrationEnvironment.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (OracleTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        try
        {
            Assert.Equal(
                3,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM user_objects
                    WHERE object_name IN (
                        'FN_EVALUATION_GRADE',
                        'SP_PUBLISH_TERM_EVALUATIONS',
                        'TRG_EVALUATIONS_DERIVE_SCORE'
                    )
                      AND status = 'VALID'
                    """));

            await InsertUserAsync(connection, transaction, MemberOneId, "evaluation_member_217_1", "考核成员甲");
            await InsertUserAsync(connection, transaction, MemberTwoId, "evaluation_member_217_2", "考核成员乙");
            await InsertUserAsync(connection, transaction, EvaluatorId, "evaluation_reviewer_217", "考核负责人");
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO clubs (club_id, club_name, club_status, created_at) VALUES (:clubId, '成员考核例程测试社', 'active', SYSDATE)",
                ("clubId", ClubId));

            Assert.Equal("优秀", await GradeAsync(connection, transaction, 320));
            Assert.Equal("良好", await GradeAsync(connection, transaction, 319));
            Assert.Equal("良好", await GradeAsync(connection, transaction, 260));
            Assert.Equal("合格", await GradeAsync(connection, transaction, 259));
            Assert.Equal("合格", await GradeAsync(connection, transaction, 200));
            Assert.Equal("待提升", await GradeAsync(connection, transaction, 199));

            await InsertEvaluationAsync(
                connection,
                transaction,
                FirstDraftId,
                MemberOneId,
                "semester",
                "2026秋",
                "draft",
                80,
                80,
                80,
                80,
                1,
                "待提升");

            Assert.Equal(
                320m,
                await ScalarDecimalAsync(
                    connection,
                    transaction,
                    "SELECT total_score FROM evaluations WHERE evaluation_id = :evaluationId",
                    ("evaluationId", FirstDraftId)));
            Assert.Equal(
                "优秀",
                await ScalarStringAsync(
                    connection,
                    transaction,
                    "SELECT grade FROM evaluations WHERE evaluation_id = :evaluationId",
                    ("evaluationId", FirstDraftId)));

            await InsertEvaluationAsync(
                connection,
                transaction,
                SecondDraftId,
                MemberTwoId,
                "semester",
                "2026秋",
                "draft",
                70,
                70,
                70,
                70,
                999,
                "优秀");
            await InsertEvaluationAsync(
                connection,
                transaction,
                OtherTermId,
                MemberOneId,
                "semester",
                "2027春",
                "draft",
                60,
                60,
                60,
                60,
                240,
                "合格");
            await InsertEvaluationAsync(
                connection,
                transaction,
                AwardEvaluationId,
                MemberOneId,
                "award",
                "2026秋",
                "draft",
                0,
                0,
                0,
                90,
                90,
                "待提升");
            await InsertEvaluationAsync(
                connection,
                transaction,
                PublishedId,
                MemberOneId,
                "semester",
                "2026秋",
                "published",
                90,
                90,
                90,
                20,
                290,
                "良好");

            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                """
                INSERT INTO evaluations (
                    evaluation_id, evaluation_type, club_id, user_id, term_name,
                    activity_score, task_score, learning_score, award_score,
                    total_score, grade, public_status, created_at
                ) VALUES (
                    :evaluationId, 'semester', :clubId, :memberId, '非法分数',
                    101, 0, 0, 0, 101, '待提升', 'draft', SYSDATE
                )
                """,
                -20232,
                ("evaluationId", InvalidScoreId),
                ("clubId", ClubId),
                ("memberId", MemberOneId));

            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                "BEGIN SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId); END;",
                -20236,
                ("clubId", 0),
                ("termName", "2026秋"),
                ("evaluatorId", EvaluatorId));
            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                "BEGIN SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId); END;",
                -20237,
                ("clubId", ClubId),
                ("termName", " "),
                ("evaluatorId", EvaluatorId));
            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                "BEGIN SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId); END;",
                -20238,
                ("clubId", ClubId),
                ("termName", "2026秋"),
                ("evaluatorId", 0));
            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                "BEGIN SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId); END;",
                -20239,
                ("clubId", 9999999),
                ("termName", "2026秋"),
                ("evaluatorId", EvaluatorId));

            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                """
                BEGIN
                    SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId);
                END;
                """,
                -20240,
                ("clubId", ClubId),
                ("termName", "2026秋"),
                ("evaluatorId", 9999999));
            Assert.Equal(
                2,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM evaluations
                    WHERE evaluation_id IN (:firstDraftId, :secondDraftId)
                      AND public_status = 'draft'
                      AND evaluator_user_id IS NULL
                    """,
                    ("firstDraftId", FirstDraftId),
                    ("secondDraftId", SecondDraftId)));

            await ExecuteAsync(
                connection,
                transaction,
                """
                BEGIN
                    SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId);
                END;
                """,
                ("clubId", ClubId),
                ("termName", "2026秋"),
                ("evaluatorId", EvaluatorId));

            Assert.Equal(
                2,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM evaluations
                    WHERE evaluation_id IN (:firstDraftId, :secondDraftId)
                      AND public_status = 'published'
                      AND evaluator_user_id = :evaluatorId
                    """,
                    ("firstDraftId", FirstDraftId),
                    ("secondDraftId", SecondDraftId),
                    ("evaluatorId", EvaluatorId)));
            Assert.Equal(
                2,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM evaluations
                    WHERE evaluation_id IN (:otherTermId, :awardEvaluationId)
                      AND public_status = 'draft'
                      AND evaluator_user_id IS NULL
                    """,
                    ("otherTermId", OtherTermId),
                    ("awardEvaluationId", AwardEvaluationId)));
            Assert.Equal(
                1,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM evaluations
                    WHERE evaluation_id = :publishedId
                      AND public_status = 'published'
                      AND evaluator_user_id IS NULL
                    """,
                    ("publishedId", PublishedId)));

            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                """
                BEGIN
                    SP_PUBLISH_TERM_EVALUATIONS(:clubId, :termName, :evaluatorId);
                END;
                """,
                -20241,
                ("clubId", ClubId),
                ("termName", "2026秋"),
                ("evaluatorId", EvaluatorId));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Task InsertUserAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        int userId,
        string username,
        string realName) =>
        ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO users (user_id, username, real_name, account_status, created_at) VALUES (:userId, :username, :realName, 'normal', SYSDATE)",
            ("userId", userId),
            ("username", username),
            ("realName", realName));

    private static Task InsertEvaluationAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        int evaluationId,
        int memberId,
        string evaluationType,
        string termName,
        string publicStatus,
        decimal activityScore,
        decimal taskScore,
        decimal learningScore,
        decimal awardScore,
        decimal totalScore,
        string grade) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO evaluations (
                evaluation_id, evaluation_type, club_id, user_id, term_name,
                activity_score, task_score, learning_score, award_score,
                total_score, grade, public_status, created_at
            ) VALUES (
                :evaluationId, :evaluationType, :clubId, :memberId, :termName,
                :activityScore, :taskScore, :learningScore, :awardScore,
                :totalScore, :grade, :publicStatus, SYSDATE
            )
            """,
            ("evaluationId", evaluationId),
            ("evaluationType", evaluationType),
            ("clubId", ClubId),
            ("memberId", memberId),
            ("termName", termName),
            ("activityScore", activityScore),
            ("taskScore", taskScore),
            ("learningScore", learningScore),
            ("awardScore", awardScore),
            ("totalScore", totalScore),
            ("grade", grade),
            ("publicStatus", publicStatus));

    private static async Task ExpectRoutineErrorAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sql,
        int expectedNumber,
        params (string Name, object Value)[] parameters)
    {
        var exception = await Record.ExceptionAsync(() => ExecuteAsync(connection, transaction, sql, parameters));
        var oracleException = Assert.IsType<OracleException>(exception);
        Assert.True(
            oracleException.Number == Math.Abs(expectedNumber) ||
            oracleException.Message.Contains($"ORA-{Math.Abs(expectedNumber)}", StringComparison.Ordinal),
            $"Expected ORA-{Math.Abs(expectedNumber)}, got {oracleException.Message}");
    }

    private static async Task ExecuteAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<decimal> ScalarDecimalAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters) =>
        Convert.ToDecimal(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<string> ScalarStringAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters) =>
        Convert.ToString(await ScalarAsync(connection, transaction, sql, parameters))!;

    private static Task<string> GradeAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        decimal totalScore) =>
        ScalarStringAsync(
            connection,
            transaction,
            "SELECT FN_EVALUATION_GRADE(:totalScore) FROM dual",
            ("totalScore", totalScore));

    private static async Task<object?> ScalarAsync(
        OracleConnection connection,
        OracleTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return await command.ExecuteScalarAsync();
    }

    private static void AddParameters(
        OracleCommand command,
        IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
