using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubHub.Api.Tests;

public sealed class ClubApplicationMaterialTests
{
    [Fact]
    public async Task CreateApplication_DoesNotRequireMaterialUrl()
    {
        await using var factory = new ClubHubWebApplicationFactory();
        const int userId = 193201;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubHubDbContext>();
            var studentRole = await db.Roles.SingleOrDefaultAsync(role => role.RoleCode == "STUDENT")
                ?? new ClubHub.Api.Data.Entities.Role
                {
                    RoleId = 193203,
                    RoleCode = "STUDENT",
                    RoleName = "普通学生",
                    RoleScope = "system",
                    CreatedAt = DateTime.UtcNow
                };
            if (db.Entry(studentRole).State == EntityState.Detached) db.Roles.Add(studentRole);
            db.Users.Add(new User
            {
                UserId = userId,
                Username = "club-application-no-material",
                PasswordHash = "not-used",
                RealName = "申请材料回归测试",
                StudentNo = "2930201",
                AccountStatus = "normal",
                CreatedAt = DateTime.UtcNow
            });
            db.UserRoles.Add(new UserRole
            {
                UserRoleId = 193202,
                UserId = userId,
                RoleId = studentRole.RoleId,
                AssignedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var tokenScope = factory.Services.CreateScope();
        var token = tokenScope.ServiceProvider.GetRequiredService<AuthTokenService>()
            .CreateToken(new User { UserId = userId, Username = "club-application-no-material" });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync("/api/v1/clubs/applications", new
        {
            name = "无材料链接测试社团",
            category = "学术科技",
            applyReason = "验证申请流程不依赖无法现场展示的外部材料地址。"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var saved = await verifyScope.ServiceProvider.GetRequiredService<ClubHubDbContext>()
            .Clubs.SingleAsync(club => club.ApplicantUserId == userId);
        Assert.Null(saved.MaterialUrl);
    }
}
