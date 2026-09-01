using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace ClubHub.Api.OracleIntegrationTests;

public sealed class DatabaseRoutineOracleTests
{
    private const int UserId = 9215001;
    private const int ReviewerId = 9215002;
    private const int ClubId = 9215001;
    private const int AccountId = 9215001;
    private const int ApplicationId = 9215001;
    private const int InsufficientApplicationId = 9215002;
    private const int VenueId = 9215001;
    private const int ReservationId = 9215001;
    private const int OverlapReservationId = 9215002;
    private const int AdjacentReservationId = 9215003;
    private const int UpdateReservationId = 9215004;

    [OracleIntegrationFact]
    public async Task BudgetRoutines_ReviewAndVenueTrigger_ProtectOracleInvariants()
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
                        'FN_BUDGET_AVAILABLE_AMOUNT',
                        'SP_REVIEW_BUDGET_APPLICATION',
                        'TRG_VENUE_RESERVATION_OVERLAP'
                    )
                      AND status = 'VALID'
                    """));
            Assert.Equal(
                1,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM user_indexes
                    WHERE index_name = 'IX_VENUE_RESERVATIONS_VENUE_ID'
                      AND table_name = 'VENUE_RESERVATIONS'
                    """));

            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO users (user_id, username, real_name, account_status, created_at) VALUES (:userId, 'routine_applicant_215', '例程申请人', 'normal', SYSDATE)",
                ("userId", UserId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO users (user_id, username, real_name, account_status, created_at) VALUES (:reviewerId, 'routine_reviewer_215', '例程审核人', 'normal', SYSDATE)",
                ("reviewerId", ReviewerId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO clubs (club_id, club_name, club_status, created_at) VALUES (:clubId, '数据库例程测试社', 'active', SYSDATE)",
                ("clubId", ClubId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO budget_accounts (account_id, club_id, fiscal_year, account_name, initial_amount, account_status) VALUES (:accountId, :clubId, '2150', '例程测试账户', 100, 'active')",
                ("accountId", AccountId),
                ("clubId", ClubId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO budget_applications (application_id, account_id, club_id, applicant_user_id, application_type, title, amount, purpose, application_status) VALUES (:applicationId, :accountId, :clubId, :userId, 'purchase', '例程测试采购', 40, '测试经费审批过程', 'pending')",
                ("applicationId", ApplicationId),
                ("accountId", AccountId),
                ("clubId", ClubId),
                ("userId", UserId));

            Assert.Equal(
                100m,
                await ScalarDecimalAsync(
                    connection,
                    transaction,
                    "SELECT FN_BUDGET_AVAILABLE_AMOUNT(:accountId) FROM dual",
                    ("accountId", AccountId)));

            await ExecuteAsync(
                connection,
                transaction,
                """
                BEGIN
                    SP_REVIEW_BUDGET_APPLICATION(:applicationId, :reviewerId, 1, '审批通过');
                END;
                """,
                ("applicationId", ApplicationId),
                ("reviewerId", ReviewerId));

            Assert.Equal(
                "approved",
                await ScalarStringAsync(
                    connection,
                    transaction,
                    "SELECT application_status FROM budget_applications WHERE application_id = :applicationId",
                    ("applicationId", ApplicationId)));
            Assert.Equal(
                1,
                await ScalarIntAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM budget_review_records WHERE application_id = :applicationId AND approved = 1",
                    ("applicationId", ApplicationId)));
            Assert.Equal(
                -40m,
                await ScalarDecimalAsync(
                    connection,
                    transaction,
                    "SELECT amount FROM budget_transactions WHERE application_id = :applicationId AND transaction_type = 'commitment'",
                    ("applicationId", ApplicationId)));
            Assert.Equal(
                60m,
                await ScalarDecimalAsync(
                    connection,
                    transaction,
                    "SELECT FN_BUDGET_AVAILABLE_AMOUNT(:accountId) FROM dual",
                    ("accountId", AccountId)));

            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO budget_applications (application_id, account_id, club_id, applicant_user_id, application_type, title, amount, purpose, application_status) VALUES (:applicationId, :accountId, :clubId, :userId, 'purchase', '余额不足测试', 1000, '应被过程拒绝', 'pending')",
                ("applicationId", InsufficientApplicationId),
                ("accountId", AccountId),
                ("clubId", ClubId),
                ("userId", UserId));
            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                """
                BEGIN
                    SP_REVIEW_BUDGET_APPLICATION(:applicationId, :reviewerId, 1, '不应通过');
                END;
                """,
                -20049,
                ("applicationId", InsufficientApplicationId),
                ("reviewerId", ReviewerId));
            Assert.Equal(
                "pending",
                await ScalarStringAsync(
                    connection,
                    transaction,
                    "SELECT application_status FROM budget_applications WHERE application_id = :applicationId",
                    ("applicationId", InsufficientApplicationId)));

            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO venues (venue_id, venue_name, venue_status, created_at) VALUES (:venueId, '例程测试教室', 'available', SYSDATE)",
                ("venueId", VenueId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO venue_reservations (reservation_id, venue_id, club_id, applicant_user_id, start_at, end_at, purpose, reservation_status, reviewer_user_id, created_at) VALUES (:reservationId, :venueId, :clubId, :userId, DATE '2026-09-01', DATE '2026-09-01' + 1/24, '首段预约', 'approved', :reviewerId, SYSDATE)",
                ("reservationId", ReservationId),
                ("venueId", VenueId),
                ("clubId", ClubId),
                ("userId", UserId),
                ("reviewerId", ReviewerId));
            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                "INSERT INTO venue_reservations (reservation_id, venue_id, club_id, applicant_user_id, start_at, end_at, purpose, reservation_status, reviewer_user_id, created_at) VALUES (:reservationId, :venueId, :clubId, :userId, DATE '2026-09-01' + 1/48, DATE '2026-09-01' + 1/16, '重叠预约', 'approved', :reviewerId, SYSDATE)",
                -20054,
                ("reservationId", OverlapReservationId),
                ("venueId", VenueId),
                ("clubId", ClubId),
                ("userId", UserId),
                ("reviewerId", ReviewerId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO venue_reservations (reservation_id, venue_id, club_id, applicant_user_id, start_at, end_at, purpose, reservation_status, reviewer_user_id, created_at) VALUES (:reservationId, :venueId, :clubId, :userId, DATE '2026-09-01' + 1/24, DATE '2026-09-01' + 1/12, '首尾相接预约', 'approved', :reviewerId, SYSDATE)",
                ("reservationId", AdjacentReservationId),
                ("venueId", VenueId),
                ("clubId", ClubId),
                ("userId", UserId),
                ("reviewerId", ReviewerId));
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO venue_reservations (reservation_id, venue_id, club_id, applicant_user_id, start_at, end_at, purpose, reservation_status, reviewer_user_id, created_at) VALUES (:reservationId, :venueId, :clubId, :userId, DATE '2026-09-01' + 1/48, DATE '2026-09-01' + 1/16, '状态变更预约', 'pending', :reviewerId, SYSDATE)",
                ("reservationId", UpdateReservationId),
                ("venueId", VenueId),
                ("clubId", ClubId),
                ("userId", UserId),
                ("reviewerId", ReviewerId));
            await ExpectRoutineErrorAsync(
                connection,
                transaction,
                "UPDATE venue_reservations SET reservation_status = 'approved' WHERE reservation_id = :reservationId",
                -20054,
                ("reservationId", UpdateReservationId));
            Assert.Equal(
                "pending",
                await ScalarStringAsync(
                    connection,
                    transaction,
                    "SELECT reservation_status FROM venue_reservations WHERE reservation_id = :reservationId",
                    ("reservationId", UpdateReservationId)));
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE venue_reservations SET start_at = DATE '2026-09-01' + 1/12, end_at = DATE '2026-09-01' + 1/8, reservation_status = 'approved' WHERE reservation_id = :reservationId",
                ("reservationId", UpdateReservationId));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

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
