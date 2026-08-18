using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiCreateForumPostRequest = Org.OpenAPITools.Models.CreateForumPostRequest;
using ApiForumPost = Org.OpenAPITools.Models.ForumPost;
using ApiModerateForumPostRequest = Org.OpenAPITools.Models.ModerateForumPostRequest;
using PermissionRole = ClubHub.Api.Services.AuthRole;

namespace ClubHub.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/clubs/{clubId:int}/forum-posts")]
public sealed class ForumPostsController : ControllerBase
{
    private const string Published = "published";
    private const string Hidden = "hidden";
    private const string ForumPostPermission = "forum:post";
    private const string ForumModeratePermission = "forum:moderate";
    private const string ClubViewPermission = "club:internal:view";

    private readonly ClubHubDbContext _db;
    private readonly AuthService _authService;

    public ForumPostsController(ClubHubDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int clubId, [FromQuery] bool includeHidden = false)
    {
        var context = await GetUserContextAsync(clubId);
        if (context.Result is not null) return context.Result;

        var canModerate = Allows(context.Roles!, ForumModeratePermission, clubId);
        if (includeHidden && !canModerate)
            return StatusCode(403, new { message = "\u5f53\u524d\u7528\u6237\u6ca1\u6709\u67e5\u770b\u5df2\u9690\u85cf\u5185\u5bb9\u7684\u6743\u9650\u3002" });

        var posts = await _db.ForumPosts.AsNoTracking().Include(post => post.User)
            .Where(post => post.ClubId == clubId)
            .OrderByDescending(post => post.IsTop).ThenByDescending(post => post.CreatedAt).ThenByDescending(post => post.PostId)
            .ToListAsync();
        var topics = posts.Where(post => post.ParentPostId is null).Where(post => includeHidden || IsPublished(post))
            .Select(topic => ToApiPost(topic, posts.Where(reply => reply.ParentPostId == topic.PostId)
                .Where(reply => includeHidden || (IsPublished(topic) && IsPublished(reply)))
                .OrderBy(reply => reply.CreatedAt).ThenBy(reply => reply.PostId)
                .Select(reply => ToApiPost(reply, [])).ToList())).ToList();
        return Ok(topics);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int clubId, [FromBody] ApiCreateForumPostRequest request)
    {
        var context = await GetUserContextAsync(clubId);
        if (context.Result is not null) return context.Result;
        if (!Allows(context.Roles!, ForumPostPermission, clubId))
            return StatusCode(403, new { message = "\u5f53\u524d\u7528\u6237\u6ca1\u6709\u53d1\u5e03\u8ba8\u8bba\u5185\u5bb9\u7684\u6743\u9650\u3002" });

        var content = request.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content)) return BadRequest(new { message = "\u5185\u5bb9\u4e0d\u80fd\u4e3a\u7a7a\u3002" });
        var isReply = request.ParentPostId is not null;
        var title = request.Title?.Trim();
        ForumPost? parent = null;
        if (isReply)
        {
            if (!string.IsNullOrWhiteSpace(title)) return BadRequest(new { message = "\u56de\u590d\u4e0d\u9700\u8981\u586b\u5199\u6807\u9898\u3002" });
            parent = await _db.ForumPosts.FirstOrDefaultAsync(post => post.PostId == request.ParentPostId!.Value);
            if (parent is null) return NotFound(new { message = "\u7236\u7ea7\u8bdd\u9898\u4e0d\u5b58\u5728\u3002" });
            if (parent.ClubId != clubId) return BadRequest(new { message = "\u4e0d\u80fd\u56de\u590d\u5176\u4ed6\u793e\u56e2\u7684\u8bdd\u9898\u3002" });
            if (parent.ParentPostId is not null) return BadRequest(new { message = "\u56de\u590d\u53ea\u80fd\u5173\u8054\u8bdd\u9898\u3002" });
            if (!IsPublished(parent)) return BadRequest(new { message = "\u5df2\u9690\u85cf\u7684\u8bdd\u9898\u4e0d\u80fd\u56de\u590d\u3002" });
        }
        else if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "\u8bdd\u9898\u6807\u9898\u4e0d\u80fd\u4e3a\u7a7a\u3002" });

        var now = DateTime.UtcNow;
        var post = new ForumPost { ClubId = clubId, UserId = context.User!.UserId, ParentPostId = parent?.PostId, Title = isReply ? null : title, Content = content, IsTop = 0, PostStatus = Published, CreatedAt = now, UpdatedAt = now };
        _db.ForumPosts.Add(post);
        await _db.SaveChangesAsync();
        post.User = context.User;
        return Created($"/api/clubs/{clubId}/forum-posts/{post.PostId}", ToApiPost(post, []));
    }

    [HttpPatch("{postId:int}/moderation")]
    public async Task<IActionResult> Moderate(int clubId, int postId, [FromBody] ApiModerateForumPostRequest request)
    {
        var context = await GetUserContextAsync(clubId);
        if (context.Result is not null) return context.Result;
        if (!Allows(context.Roles!, ForumModeratePermission, clubId))
            return StatusCode(403, new { message = "\u5f53\u524d\u7528\u6237\u6ca1\u6709\u8ba8\u8bba\u533a\u7ba1\u7406\u6743\u9650\u3002" });
        var post = await _db.ForumPosts.Include(item => item.User).FirstOrDefaultAsync(item => item.PostId == postId && item.ClubId == clubId);
        if (post is null) return NotFound(new { message = "\u8ba8\u8bba\u533a\u5185\u5bb9\u4e0d\u5b58\u5728\u3002" });
        if (request.PostStatus is not ApiModerateForumPostRequest.PostStatusEnum.PublishedEnum and not ApiModerateForumPostRequest.PostStatusEnum.HiddenEnum)
            return BadRequest(new { message = "\u5185\u5bb9\u72b6\u6001\u53ea\u80fd\u662f published \u6216 hidden\u3002" });
        if (post.ParentPostId is not null && request.IsTop) return BadRequest(new { message = "\u56de\u590d\u4e0d\u80fd\u7f6e\u9876\u3002" });

        post.IsTop = post.ParentPostId is null && request.IsTop ? 1 : 0;
        post.PostStatus = request.PostStatus == ApiModerateForumPostRequest.PostStatusEnum.HiddenEnum ? Hidden : Published;
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToApiPost(post, []));
    }

    private async Task<UserContext> GetUserContextAsync(int clubId)
    {
        var userId = User.GetUserId();
        if (userId is null) return new(Unauthorized(new { message = "\u767b\u5f55\u72b6\u6001\u5df2\u5931\u6548\u3002" }), null, null);
        var user = await _db.Users.FindAsync(userId.Value);
        if (user is null) return new(NotFound(new { message = "\u5f53\u524d\u7528\u6237\u4e0d\u5b58\u5728\u3002" }), null, null);
        if (!UsersController.IsActive(user.AccountStatus)) return new(StatusCode(403, new { message = "\u5f53\u524d\u8d26\u53f7\u4e0d\u53ef\u7528\u3002" }), null, null);
        if (!await _db.Clubs.AnyAsync(club => club.ClubId == clubId)) return new(NotFound(new { message = "\u793e\u56e2\u4e0d\u5b58\u5728\u3002" }), null, null);
        var roles = await _authService.GetPermissionRolesAsync(user.UserId);
        var canModerate = Allows(roles, ForumModeratePermission, clubId);
        if (!canModerate && !await IsActiveMemberAsync(user.UserId, clubId)) return new(StatusCode(403, new { message = "\u53ea\u6709\u5f53\u524d\u6709\u6548\u6210\u5458\u53ef\u8bbf\u95ee\u3002" }), null, null);
        if (!Allows(roles, ClubViewPermission, clubId) && !Allows(roles, ForumPostPermission, clubId) && !canModerate) return new(StatusCode(403, new { message = "\u6ca1\u6709\u8bbf\u95ee\u6743\u9650\u3002" }), null, null);
        return new(null, user, roles);
    }

    private Task<bool> IsActiveMemberAsync(int userId, int clubId)
    {
        var today = DateTime.UtcNow.Date;
        return _db.ClubMembers.AnyAsync(member => member.UserId == userId && member.ClubId == clubId && (member.MemberStatus == null || member.MemberStatus == "" || member.MemberStatus.ToLower() == "active" || member.MemberStatus.ToLower() == "normal" || member.MemberStatus.ToLower() == "enabled") && (member.TermStart == null || member.TermStart <= today) && (member.TermEnd == null || member.TermEnd >= today));
    }

    private static bool Allows(IReadOnlyList<PermissionRole> roles, string permission, int clubId) => roles.Any(role => role.Permissions.Contains("*") || (role.Permissions.Contains(permission) && (role.ClubId is null || role.ClubId == clubId || role.ClubIds.Contains(clubId))));
    private static bool IsPublished(ForumPost post) => string.Equals(post.PostStatus, Published, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(post.PostStatus);
    private static ApiForumPost ToApiPost(ForumPost post, List<ApiForumPost> replies) => new() { Id = post.PostId, ClubId = post.ClubId, UserId = post.UserId, UserName = post.User is null ? null : (string.IsNullOrWhiteSpace(post.User.RealName) ? post.User.Username : post.User.RealName), ParentPostId = post.ParentPostId, Title = post.Title, Content = post.Content, IsTop = post.IsTop != 0, PostStatus = IsPublished(post) ? ApiForumPost.PostStatusEnum.PublishedEnum : ApiForumPost.PostStatusEnum.HiddenEnum, CreatedAt = post.CreatedAt, UpdatedAt = post.UpdatedAt, Replies = replies };
    private sealed record UserContext(IActionResult? Result, User? User, IReadOnlyList<PermissionRole>? Roles);
}
