using Oracle.ManagedDataAccess.Client;

namespace ClubHub.Api.OracleIntegrationTests;

public sealed class DataQualityAuditOracleTests
{
    [OracleIntegrationFact]
    public async Task DataQualityAudit_ReturnsNoFindingsForIsolatedBaseline()
    {
        await using var connection = new OracleConnection(OracleIntegrationEnvironment.ConnectionString);
        await connection.OpenAsync();

        foreach (var auditQuery in ReadStatements(
                     ReadRepositoryFile("database", "seeds", "009_data_quality_audit.sql")))
        {
            Assert.Equal(0, await ScalarIntAsync(connection, $"SELECT COUNT(*) FROM ({auditQuery})"));
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

    private static async Task<int> ScalarIntAsync(OracleConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
