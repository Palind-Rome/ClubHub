using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClubHub.Api.Tests;

public sealed class PasswordChangeEndpointTests
{
    private const string CurrentPassword = "ClubHub123";
    private const string NewPassword = "NewClubHub456";

    [Fact]
    public async Task ChangePasswordUpdatesHashWritesSafeLogAndRequiresNewPasswordForLogin()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await ChangePasswordAsync(client, CurrentPassword, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Username == "password-user");
        Assert.False(PasswordHasher.Verify(CurrentPassword, user.PasswordHash));
        Assert.True(PasswordHasher.Verify(NewPassword, user.PasswordHash));

        var operation = await db.OperationLogs.SingleAsync(log =>
            log.UserId == user.UserId && log.OperationType == "password_changed");
        Assert.Equal("auth", operation.ModuleName);
        Assert.Equal("USERS", operation.TargetTable);
        Assert.Equal(user.UserId, operation.TargetId);
    }

    [Fact]
    public async Task ChangePasswordRejectsWrongCurrentPasswordWithoutChangingHash()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await ChangePasswordAsync(client, "wrong-password", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertPasswordRemainsAsync(factory, CurrentPassword);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData(CurrentPassword)]
    public async Task ChangePasswordRejectsInvalidOrRepeatedNewPassword(string newPassword)
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await ChangePasswordAsync(client, CurrentPassword, newPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertPasswordRemainsAsync(factory, CurrentPassword);
    }

    [Fact]
    public async Task ChangePasswordRequiresBearerIdentity()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await ChangePasswordAsync(client, CurrentPassword, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordRejectsDisabledAccount()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, "disabled");

        var response = await ChangePasswordAsync(client, CurrentPassword, NewPassword);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertPasswordRemainsAsync(factory, CurrentPassword);
    }

    [Fact]
    public async Task ChangePasswordRevokesAllSessionsBeforePersistingNewPassword()
    {
        var sessions = new RecordingAuthSessionService();
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthSessionService>();
                services.AddSingleton<IAuthSessionService>(sessions);
            }));
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await ChangePasswordAsync(client, CurrentPassword, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(sessions.RevokedUserIds);
    }

    [Fact]
    public async Task ChangePasswordFailsSafelyWhenSessionRevocationIsUnavailable()
    {
        var sessions = new RecordingAuthSessionService { FailRevocation = true };
        await using var baseFactory = new ClubHubWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthSessionService>();
                services.AddSingleton<IAuthSessionService>(sessions);
            }));
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await ChangePasswordAsync(client, CurrentPassword, NewPassword);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertPasswordRemainsAsync(factory, CurrentPassword);
    }

    [Fact]
    public void PasswordHashIsConfiguredAsConcurrencyToken()
    {
        using var factory = new ClubHubWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();

        var property = db.Model.FindEntityType(typeof(User))?.FindProperty(nameof(User.PasswordHash));

        Assert.NotNull(property);
        Assert.True(property.IsConcurrencyToken);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string accountStatus = "normal")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var user = new User
        {
            UserId = 21301,
            Username = "password-user",
            PasswordHash = PasswordHasher.Hash(CurrentPassword),
            RealName = "密码测试用户",
            StudentNo = "2450213",
            AccountStatus = accountStatus,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(user);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> ChangePasswordAsync(
        HttpClient client,
        string currentPassword,
        string newPassword) =>
        client.PutAsJsonAsync("/api/v1/users/me/password", new
        {
            currentPassword,
            newPassword
        });

    private static async Task AssertPasswordRemainsAsync(
        WebApplicationFactory<Program> factory,
        string expectedPassword)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Username == "password-user");
        Assert.True(PasswordHasher.Verify(expectedPassword, user.PasswordHash));
        Assert.Empty(await db.OperationLogs
            .Where(log => log.UserId == user.UserId && log.OperationType == "password_changed")
            .ToListAsync());
    }

    private sealed class RecordingAuthSessionService : IAuthSessionService
    {
        public bool Enabled => true;

        public bool FailRevocation { get; init; }

        public List<int> RevokedUserIds { get; } = [];

        public Task CreateAsync(
            string token,
            AuthTokenPrincipal principal,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AuthSessionValidation> ValidateAndRefreshAsync(
            string token,
            AuthTokenPrincipal principal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthSessionValidation.Valid);

        public Task RevokeAsync(
            string token,
            AuthTokenPrincipal principal,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RevokeAllAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (FailRevocation)
            {
                throw new TimeoutException("session store unavailable");
            }

            RevokedUserIds.Add(userId);
            return Task.CompletedTask;
        }
    }
}
