using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClubHub.Api.Tests;

public sealed class NoticeReadModelTests
{
    [Fact]
    public void NoticeRead_UsesDatabaseSequenceAndScopedUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ClubHubDbContext>()
            .UseOracle("Data Source=ClubHubModelTests")
            .Options;
        using var db = new ClubHubDbContext(options);

        var entity = db.Model.FindEntityType(typeof(NoticeRead));
        Assert.NotNull(entity);

        var readId = entity.FindProperty(nameof(NoticeRead.ReadId));
        Assert.NotNull(readId);
        Assert.Equal("SEQ_NOTICE_READS.NEXTVAL", readId.GetDefaultValueSql());

        var uniqueIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(NoticeRead.NoticeId), nameof(NoticeRead.UserId)]));
        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal("UQ_NOTICE_READS_NOTICE_USER", uniqueIndex.GetDatabaseName());
    }
}
