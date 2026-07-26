using System.Reflection;
using ClubHub.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class PermissionInvalidationRegistrationTests
    : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;

    public PermissionInvalidationRegistrationTests(ClubHubWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public void InterceptorsShareCoordinatorWithinScope_AndCoordinatorIsIsolatedAcrossScopes()
    {
        using var firstScope = _factory.Services.CreateScope();
        var firstCoordinator =
            firstScope.ServiceProvider.GetRequiredService<PermissionInvalidationCoordinator>();
        var saveChangesInterceptor =
            firstScope.ServiceProvider.GetRequiredService<PermissionInvalidationInterceptor>();
        var transactionInterceptor =
            firstScope.ServiceProvider.GetRequiredService<PermissionTransactionInterceptor>();

        Assert.Same(firstCoordinator, CoordinatorOf(saveChangesInterceptor));
        Assert.Same(firstCoordinator, CoordinatorOf(transactionInterceptor));

        using var secondScope = _factory.Services.CreateScope();
        var secondCoordinator =
            secondScope.ServiceProvider.GetRequiredService<PermissionInvalidationCoordinator>();
        Assert.NotSame(firstCoordinator, secondCoordinator);
    }

    private static PermissionInvalidationCoordinator CoordinatorOf(object interceptor)
    {
        var field = interceptor.GetType().GetField(
            "_coordinator",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<PermissionInvalidationCoordinator>(field?.GetValue(interceptor));
    }
}
