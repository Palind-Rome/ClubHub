using Oracle.ManagedDataAccess.Client;

namespace ClubHub.Api.OracleIntegrationTests;

public sealed class DefenseDemoSeedOracleTests
{
    private const int DefenseId = 8101;

    [OracleIntegrationFact]
    public async Task DefenseSeed_IsIdempotent_PreservesValidTitles_AndPassesAudit()
    {
        await using var connection = new OracleConnection(OracleIntegrationEnvironment.ConnectionString);
        await connection.OpenAsync();
        var probeId = Random.Shared.Next(900_000, 999_999);

        try
        {
            await ExecuteScriptAsync(connection, ReadRepositoryFile("database", "seeds", "008_defense_demo.sql"));
            await InsertTitlePreservationProbesAsync(connection, probeId);
            await ExecuteScriptAsync(connection, ReadRepositoryFile("database", "seeds", "008_defense_demo.sql"));

            foreach (var (table, idColumn) in DefenseRecords())
            {
                Assert.Equal(
                    1,
                    await ScalarIntAsync(
                        connection,
                        $"SELECT COUNT(*) FROM {table} WHERE {idColumn} = :id",
                        DefenseId));
            }

            Assert.Equal(
                "应保留的通知标题",
                await ScalarStringAsync(
                    connection,
                    "SELECT title FROM notices WHERE notice_id = :id",
                    probeId));
            Assert.Equal(
                "应保留的学习资料标题",
                await ScalarStringAsync(
                    connection,
                    "SELECT title FROM learning_items WHERE item_id = :id",
                    probeId));

            foreach (var auditQuery in ReadStatements(ReadRepositoryFile("database", "seeds", "009_defense_data_audit.sql")))
            {
                Assert.Equal(0, await ScalarIntAsync(connection, $"SELECT COUNT(*) FROM ({auditQuery})"));
            }
        }
        finally
        {
            await ExecuteNonQueryAsync(connection, "DELETE FROM notices WHERE notice_id = :id", probeId);
            await ExecuteNonQueryAsync(connection, "DELETE FROM learning_items WHERE item_id = :id", probeId);
            await ExecuteNonQueryAsync(connection, "COMMIT");
        }
    }

    private static IEnumerable<(string Table, string IdColumn)> DefenseRecords()
    {
        yield return ("activities", "activity_id");
        yield return ("projects", "project_id");
        yield return ("learning_items", "item_id");
        yield return ("notices", "notice_id");
        yield return ("forum_posts", "post_id");
    }

    private static async Task InsertTitlePreservationProbesAsync(OracleConnection connection, int probeId)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO notices (
              notice_id, club_id, publisher_user_id, notice_type, title, content,
              target_type, target_id, publish_at, expire_at, notice_status
            ) VALUES (
              :id, 1, 3, 'event', '应保留的通知标题', NULL,
              'club', 1, SYSDATE, SYSDATE + 1, 'published'
            )
            """,
            probeId);
        await ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO learning_items (
              item_id, club_id, uploader_user_id, title, item_type, category_name,
              description, visibility, download_permission, item_status, created_at
            ) VALUES (
              :id, 1, 3, '应保留的学习资料标题', 'resource', '回归测试',
              NULL, 'club', 'member', 'published', SYSDATE
            )
            """,
            probeId);
        await ExecuteNonQueryAsync(connection, "COMMIT");
    }

    private static async Task ExecuteScriptAsync(OracleConnection connection, string script)
    {
        foreach (var statement in ReadStatements(script))
        {
            await ExecuteNonQueryAsync(connection, statement);
        }
    }

    private static IEnumerable<string> ReadStatements(string script) =>
        string.Join(
                Environment.NewLine,
                script.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. segments]));
    }

    private static async Task<int> ScalarIntAsync(
        OracleConnection connection,
        string sql,
        int? id = null)
    {
        await using var command = CreateCommand(connection, sql, id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(
        OracleConnection connection,
        string sql,
        int id)
    {
        await using var command = CreateCommand(connection, sql, id);
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteNonQueryAsync(
        OracleConnection connection,
        string sql,
        int? id = null)
    {
        await using var command = CreateCommand(connection, sql, id);
        await command.ExecuteNonQueryAsync();
    }

    private static OracleCommand CreateCommand(OracleConnection connection, string sql, int? id)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.BindByName = true;
        if (id is not null)
        {
            command.Parameters.Add("id", OracleDbType.Int32).Value = id.Value;
        }
        return command;
    }
}
