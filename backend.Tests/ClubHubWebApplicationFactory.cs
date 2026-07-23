using ClubHub.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClubHub.Api.Tests;

public sealed class ClubHubWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TokenSigningKey"] = "ClubHub.Tests.TokenSigningKey",
                ["ConnectionStrings:Default"] = "Tests must not use the Oracle connection"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ClubHubDbContext>();
            services.RemoveAll<DbContextOptions<ClubHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ClubHubDbContext>>();
            services.RemoveAll<IDatabaseProvider>();
            services.AddDbContext<ClubHubDbContext>(options =>
                options.UseInMemoryDatabase($"ClubHubTests-{Guid.NewGuid():N}"));
        });
    }
}
