using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ForumPostsAuthorizationTests : IClassFixture<ClubHubWebApplicationFactory>
{
    private readonly ClubHubWebApplicationFactory _factory;
    private static int _sequence;

    public ForumPostsAuthorizationTests(ClubHubWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateTopic_ActiveMember_CreatesTopic()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: false);
        using var response = await client.PostAsync($"/api/v1/clubs/{clubId}/forum-posts", Json("{\"title\":\"topic\",\"content\":\"body\"}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ForumEndpoints_WithoutBearerToken_ReturnUnauthorized()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/clubs/1/forum-posts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTopic_NonMember_IsForbidden()
    {
        var (client, clubId) = await SeedAsync(member: false, moderate: false);
        using var response = await client.PostAsync($"/api/v1/clubs/{clubId}/forum-posts", Json("{\"title\":\"topic\",\"content\":\"body\"}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateReply_ToReply_IsRejected()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: false);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        var reply = await PostAndReadId(client, clubId, $"{{\"parentPostId\":{topic},\"content\":\"reply\"}}");
        using var response = await client.PostAsync($"/api/v1/clubs/{clubId}/forum-posts", Json($"{{\"parentPostId\":{reply},\"content\":\"nested\"}}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ModerateReply_WithTopFlag_IsRejected()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: true);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        var reply = await PostAndReadId(client, clubId, $"{{\"parentPostId\":{topic},\"content\":\"reply\"}}");
        using var response = await client.PatchAsync($"/api/v1/clubs/{clubId}/forum-posts/{reply}", Json("{\"isTop\":true,\"postStatus\":\"published\"}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ModerateTopic_HideThenRestore_UpdatesStatus()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: true);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        using var hidden = await client.PatchAsync($"/api/v1/clubs/{clubId}/forum-posts/{topic}", Json("{\"isTop\":true,\"postStatus\":\"hidden\"}"));
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        using var hiddenDocument = System.Text.Json.JsonDocument.Parse(
            await hidden.Content.ReadAsStringAsync());
        Assert.Equal("hidden", hiddenDocument.RootElement.GetProperty("postStatus").GetString());

        using var restored = await client.PatchAsync($"/api/v1/clubs/{clubId}/forum-posts/{topic}", Json("{\"isTop\":false,\"postStatus\":\"published\"}"));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var restoredDocument = System.Text.Json.JsonDocument.Parse(
            await restored.Content.ReadAsStringAsync());
        Assert.Equal("published", restoredDocument.RootElement.GetProperty("postStatus").GetString());
    }

    [Fact]
    public async Task GetPosts_HiddenTopic_IsExcludedByDefault()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: true);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        using var hidden = await client.PatchAsync($"/api/v1/clubs/{clubId}/forum-posts/{topic}", Json("{\"isTop\":false,\"postStatus\":\"hidden\"}"));
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);

        using var response = await client.GetAsync($"/api/v1/clubs/{clubId}/forum-posts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DeleteTopic_ByOwner_Succeeds()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: false);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        using var response = await client.DeleteAsync($"/api/v1/clubs/{clubId}/forum-posts/{topic}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTopic_ByNonOwnerNonModerator_IsForbidden()
    {
        var (ownerClient, clubId) = await SeedAsync(member: true, moderate: false);
        var otherClient = await SeedAdditionalMemberAsync(clubId);

        var topic = await PostAndReadId(ownerClient, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        using var response = await otherClient.DeleteAsync($"/api/v1/clubs/{clubId}/forum-posts/{topic}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTopic_ByModerator_Succeeds()
    {
        var (ownerClient, clubId) = await SeedAsync(member: true, moderate: false);
        var (moderatorClient, _) = await SeedAsync(member: false, moderate: true);
        var topic = await PostAndReadId(ownerClient, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        using var response = await moderatorClient.DeleteAsync(
            $"/api/v1/clubs/{clubId}/forum-posts/{topic}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTopic_DeletesRepliesAlso()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: false);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        await PostAndReadId(client, clubId, $"{{\"parentPostId\":{topic},\"content\":\"reply1\"}}");
        await PostAndReadId(client, clubId, $"{{\"parentPostId\":{topic},\"content\":\"reply2\"}}");

        using var beforeDelete = await client.GetAsync($"/api/v1/clubs/{clubId}/forum-posts");
        using var beforeDocument = System.Text.Json.JsonDocument.Parse(await beforeDelete.Content.ReadAsStringAsync());
        var repliesBeforeDelete = beforeDocument.RootElement[0].GetProperty("replies").GetArrayLength();
        Assert.Equal(2, repliesBeforeDelete);

        using var deleteResponse = await client.DeleteAsync($"/api/v1/clubs/{clubId}/forum-posts/{topic}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var afterDelete = await client.GetAsync($"/api/v1/clubs/{clubId}/forum-posts");
        using var afterDocument = System.Text.Json.JsonDocument.Parse(await afterDelete.Content.ReadAsStringAsync());
        Assert.Equal(0, afterDocument.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DeleteReply_ByOwner_Succeeds()
    {
        var (client, clubId) = await SeedAsync(member: true, moderate: false);
        var topic = await PostAndReadId(client, clubId, "{\"title\":\"topic\",\"content\":\"body\"}");
        var reply = await PostAndReadId(client, clubId, $"{{\"parentPostId\":{topic},\"content\":\"reply\"}}");

        using var response = await client.DeleteAsync($"/api/v1/clubs/{clubId}/forum-posts/{reply}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<(HttpClient Client, int ClubId)> SeedAsync(bool member, bool moderate)
    {
        var baseId = 9000 + Interlocked.Increment(ref _sequence) * 10;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        db.Add(new User { UserId = baseId, Username = $"forum-{baseId}", PasswordHash = "unused", RealName = "Forum Test", AccountStatus = "normal", CreatedAt = now });
        db.Add(new Club { ClubId = baseId + 1, ClubName = "Forum Club", ClubStatus = "active", CreatedAt = now });
        db.Add(new Role { RoleId = baseId + 2, RoleCode = moderate ? "SYSTEM_ADMIN" : "CLUB_MEMBER", RoleName = "Test Role", RoleScope = moderate ? "system" : "club", CreatedAt = now });
        db.Add(new UserRole { UserRoleId = baseId + 3, UserId = baseId, RoleId = baseId + 2, ClubId = moderate ? null : baseId + 1, AssignedAt = now });
        if (member) db.Add(new ClubMember { MemberId = baseId + 4, UserId = baseId, ClubId = baseId + 1, MemberStatus = "active", JoinAt = now });
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(new User { UserId = baseId, Username = "forum" });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, baseId + 1);
    }

    private async Task<(HttpClient Client, int ClubId, int UserId)> SeedForMultiUserTestAsync()
    {
        var baseId = 9000 + Interlocked.Increment(ref _sequence) * 10;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        var userId = baseId;
        var clubId = baseId + 1;
        var roleId = baseId + 2;

        db.Add(new User { UserId = userId, Username = $"forum-{baseId}", PasswordHash = "unused", RealName = "Forum Test", AccountStatus = "normal", CreatedAt = now });
        db.Add(new Club { ClubId = clubId, ClubName = "Forum Club", ClubStatus = "active", CreatedAt = now });
        db.Add(new Role { RoleId = roleId, RoleCode = "CLUB_MEMBER", RoleName = "Member", RoleScope = "club", CreatedAt = now });
        db.Add(new UserRole { UserRoleId = baseId + 3, UserId = userId, RoleId = roleId, ClubId = clubId, AssignedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 4, UserId = userId, ClubId = clubId, MemberStatus = "active", JoinAt = now });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(new User { UserId = userId, Username = $"forum-{baseId}" });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, clubId, userId);
    }

    private async Task<HttpClient> SeedAdditionalMemberAsync(int clubId)
    {
        var baseId = 9000 + Interlocked.Increment(ref _sequence) * 10;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
        var now = DateTime.UtcNow;
        var userId = baseId;
        var roleId = baseId + 1;

        db.Add(new User { UserId = userId, Username = $"forum-{baseId}", PasswordHash = "unused", RealName = "Forum Test", AccountStatus = "normal", CreatedAt = now });
        db.Add(new Role { RoleId = roleId, RoleCode = "CLUB_MEMBER", RoleName = "Member", RoleScope = "club", CreatedAt = now });
        db.Add(new UserRole { UserRoleId = baseId + 2, UserId = userId, RoleId = roleId, ClubId = clubId, AssignedAt = now });
        db.Add(new ClubMember { MemberId = baseId + 3, UserId = userId, ClubId = clubId, MemberStatus = "active", JoinAt = now });
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateToken(new User { UserId = userId, Username = $"forum-{baseId}" });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static async Task<int> PostAndReadId(HttpClient client, int clubId, string body)
    {
        using var response = await client.PostAsync($"/api/v1/clubs/{clubId}/forum-posts", Json(body));
        Assert.True(response.IsSuccessStatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetInt32();
    }
}
