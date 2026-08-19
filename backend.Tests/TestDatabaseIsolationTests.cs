using ClubHub.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class TestDatabaseIsolationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public TestDatabaseIsolationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void ApiTestsUseInMemoryProviderInsteadOfOracle()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
        Assert.False(db.Database.IsOracle());
    }
}
