using ClubHub.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClubHub.Api.OracleIntegrationTests;

public sealed class JoinableDbTransactionOracleTests
{
    [OracleIntegrationFact]
    public async Task CommitAsync_ReleasesSavepoint_AndOuterCommitPersistsWrite()
    {
        await using var db = CreateContext();
        var table = CreateTableName();
        await CreateTableAsync(db, table);

        try
        {
            await using (var outer = await db.Database.BeginTransactionAsync())
            {
                await using (var joined = await db.Database.BeginJoinableTransactionAsync())
                {
                    Assert.False(joined.OwnsTransaction);
                    await InsertAsync(db, table, 1);
                    await joined.CommitAsync();
                }

                await outer.CommitAsync();
            }

            Assert.Equal(1, await CountAsync(db, table));
        }
        finally
        {
            await DropTableAsync(db, table);
        }
    }

    [OracleIntegrationFact]
    public async Task RollbackAsync_RollsBackToSavepoint_WithoutRollingBackOuterTransaction()
    {
        await using var db = CreateContext();
        var table = CreateTableName();
        await CreateTableAsync(db, table);

        try
        {
            await using (var outer = await db.Database.BeginTransactionAsync())
            {
                await using (var joined = await db.Database.BeginJoinableTransactionAsync())
                {
                    await InsertAsync(db, table, 1);
                    await joined.RollbackAsync();
                }

                Assert.Equal(0, await CountAsync(db, table));
                await InsertAsync(db, table, 2);
                await outer.CommitAsync();
            }

            Assert.Equal(1, await CountAsync(db, table));
        }
        finally
        {
            await DropTableAsync(db, table);
        }
    }

    [OracleIntegrationFact]
    public async Task DisposeAsync_RollsBackIncompleteSavepoint_WithoutRollingBackOuterTransaction()
    {
        await using var db = CreateContext();
        var table = CreateTableName();
        await CreateTableAsync(db, table);

        try
        {
            await using (var outer = await db.Database.BeginTransactionAsync())
            {
                await using (var joined = await db.Database.BeginJoinableTransactionAsync())
                {
                    await InsertAsync(db, table, 1);
                }

                Assert.Equal(0, await CountAsync(db, table));
                await InsertAsync(db, table, 2);
                await outer.CommitAsync();
            }

            Assert.Equal(1, await CountAsync(db, table));
        }
        finally
        {
            await DropTableAsync(db, table);
        }
    }

    private static OracleTransactionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OracleTransactionDbContext>()
            .UseOracle(OracleIntegrationEnvironment.ConnectionString)
            .Options;
        return new OracleTransactionDbContext(options);
    }

    private static string CreateTableName() =>
        $"CH_TX_{Guid.NewGuid():N}"[..27].ToUpperInvariant();

    private static Task CreateTableAsync(DbContext db, string table) =>
        ExecuteNonQueryAsync(
            db,
            $"CREATE TABLE {table} (probe_id NUMBER(10) NOT NULL PRIMARY KEY)");

    private static Task InsertAsync(DbContext db, string table, int id) =>
        ExecuteNonQueryAsync(
            db,
            $"INSERT INTO {table} (probe_id) VALUES (:probeId)",
            id);

    private static async Task<int> CountAsync(DbContext db, string table)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        if (db.Database.CurrentTransaction is { } transaction)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task DropTableAsync(DbContext db, string table)
    {
        if (db.Database.CurrentTransaction is { } transaction)
        {
            await transaction.RollbackAsync();
        }

        await ExecuteNonQueryAsync(db, $"DROP TABLE {table} PURGE");
    }

    private static async Task ExecuteNonQueryAsync(
        DbContext db,
        string sql,
        int? probeId = null)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (db.Database.CurrentTransaction is { } transaction)
        {
            command.Transaction = transaction.GetDbTransaction();
        }
        if (probeId is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "probeId";
            parameter.Value = probeId.Value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    private sealed class OracleTransactionDbContext(
        DbContextOptions<OracleTransactionDbContext> options) : DbContext(options);
}
