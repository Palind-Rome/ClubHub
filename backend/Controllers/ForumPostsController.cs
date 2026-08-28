using ClubHub.Api.Data;
using ClubHub.Api.Data.Entities;
using ClubHub.Api.Infrastructure.Rest;
using ClubHub.Api.Services;
using ClubHub.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiCreateForumPostRequest = Org.OpenAPITools.Models.CreateForumPostRequest;
using ApiForumPost = Org.OpenAPITools.Models.ForumPost;
using ApiModerateForumPostRequest = Org.OpenAPITools.Models.ModerateForumPostRequest;
using ApiForumImageUploadResponse = Org.OpenAPITools.Models.ForumImageUploadResponse;
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
    private readonly ProjectMembershipService _projectMembershipService;
    private readonly ForumImageUploadService _imageUploadService;

    public ForumPostsController(
        ClubHubDbContext db,
        AuthService authService,
        ProjectMembershipService projectMembershipService,
        ForumImageUploadService imageUploadService)
    {
        _db = db;
        _authService = authService;
        _projectMembershipService = projectMembershipService;
        _imageUploadService = imageUploadService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int clubId, [FromQuery] bool includeHidden = false)
    {
        var context = await GetUserContextAsync(clubId);
        if (context.Result is not null) return context.Result;

        var canModerate = Allows(context.Roles!, ForumModeratePermission, clubId);
        if (includeHidden && !canModerate)
            return StatusCode(403, new { message = "当前用户没有查看已隐藏内容的权限。" });

        var posts = await _db.ForumPosts.AsNoTracking().Include(post => post.User)
            .Where(post => post.ClubId == clubId)
            .OrderByDescending(post => post.IsTop).ThenByDescending(post => post.CreatedAt).ThenByDescending(post => post.PostId)
            .ToListAsync();
        var topics = posts.Where(post => post.ParentPostId is null).Where(post => includeHidden || IsPublished(post))
            .Select(topic => ToApiPost(topic, BuildNestedReplies(posts, topic.PostId, includeHidden))).ToList();
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
            if (!IsPublished(parent)) return BadRequest(new { message = "\u5df2\u9690\u85cf\u7684\u5185\u5bb9\u4e0d\u80fd\u56de\u590d\u3002" });
        }
        else if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "\u8bdd\u9898\u6807\u9898\u4e0d\u80fd\u4e3a\u7a7a\u3002" });

        if (content.Length < 1 || content.Length > 4000)
            return BadRequest(new { message = "\u6b63\u6587\u9577\u5ea6\u5fc5\u9808\u4e3a 1-4000 \u5b57\u7b26\u3002" });
        if (!isReply && (title.Length < 1 || title.Length > 120))
            return BadRequest(new { message = "\u6807\u9898\u9577\u5ea6\u5fc5\u9808\u4e3a 1-120 \u5b57\u7b26\u3002" });

        var now = DateTime.UtcNow;
        var post = new ForumPost { ClubId = clubId, UserId = context.User!.UserId, ParentPostId = parent?.PostId, Title = isReply ? null : title, Content = content, IsTop = 0, PostStatus = Published, CreatedAt = now, UpdatedAt = now };
        _db.ForumPosts.Add(post);
        await _db.SaveChangesAsync();
        post.User = context.User;
        return Created($"/api/v1/clubs/{clubId}/forum-posts/{post.PostId}", ToApiPost(post, []));
    }

    [HttpPatch("{postId:int}")]
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

    [HttpDelete("{postId:int}")]
    public async Task<IActionResult> Delete(int clubId, int postId)
    {
        var context = await GetUserContextAsync(clubId);
        if (context.Result is not null) return context.Result;

        var post = await _db.ForumPosts.FirstOrDefaultAsync(item => item.PostId == postId && item.ClubId == clubId);
        if (post is null) return NotFound(new { message = "\u8ba8\u8bba\u533a\u5185\u5bb9\u4e0d\u5b58\u5728\u3002" });

        var canModerate = Allows(context.Roles!, ForumModeratePermission, clubId);
        var isOwner = post.UserId == context.User!.UserId;

        if (!canModerate && !isOwner)
            return StatusCode(403, new { message = "\u4f60\u65e0\u6743\u524a\u9664\u8fd9\u4e2a\u5185\u5bb9\u3002" });

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isTopicDelete = post.ParentPostId is null;

        var descendantsToDelete = new List<ForumPost>();
        if (isTopicDelete)
        {
            var allForumPosts = await _db.ForumPosts
                .Where(item => item.ClubId == clubId)
                .ToListAsync();
            var repliesByParent = allForumPosts.ToLookup(item => item.ParentPostId);

            var toProcess = new Queue<int>();
            toProcess.Enqueue(postId);
            while (toProcess.Count > 0)
            {
                var currentId = toProcess.Dequeue();
                var children = repliesByParent[currentId];
                foreach (var child in children)
                {
                    descendantsToDelete.Add(child);
                    toProcess.Enqueue(child.PostId);
                }
            }
        }

        foreach (var descendant in descendantsToDelete)
        {
            _db.ForumPosts.Remove(descendant);
            _db.OperationLogs.Add(new OperationLog
            {
                UserId = context.User.UserId,
                ModuleName = "forum",
                OperationType = "reply_deleted",
                TargetTable = "FORUM_POSTS",
                TargetId = descendant.PostId,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.ForumPosts.Remove(post);
        _db.OperationLogs.Add(new OperationLog
        {
            UserId = context.User.UserId,
            ModuleName = "forum",
            OperationType = isTopicDelete ? "topic_deleted" : "reply_deleted",
            TargetTable = "FORUM_POSTS",
            TargetId = postId,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("upload-image")]
    public async Task<IActionResult> UploadImage(int clubId, IFormFile? image)
    {
        var context = await GetUserContextAsync(clubId);
        if (context.Result is not null) return context.Result;
        if (!Allows(context.Roles!, ForumPostPermission, clubId))
            return StatusCode(403, new { message = "\u5f53\u524d\u7528\u6237\u6ca1\u6709\u53d1\u5e03\u8ba8\u8bba\u5185\u5bb9\u7684\u6743\u9650\u3002" });

        if (image == null)
            return BadRequest(new { message = "\u6587\u4ef6\u4e0d\u5b58\u5728" });

        var result = await _imageUploadService.UploadAsync(
            clubId,
            image,
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            return result.FailureKind switch
            {
                UploadFailureKind.InvalidFile =>
                    BadRequest(new { message = result.ErrorMessage ?? "\u6587\u4ef6\u4e0d\u7b26\u5408\u8981\u6c42" }),
                UploadFailureKind.TooLarge =>
                    StatusCode(413, new { message = result.ErrorMessage ?? "\u6587\u4ef6\u8fc7\u5927" }),
                UploadFailureKind.Storage =>
                    StatusCode(500, new { message = "\u4e0a\u4f20\u5931\u8d25" }),
                _ => StatusCode(500, new { message = "\u4e0a\u4f20\u5931\u8d25" })
            };
        }

        var response = new ApiForumImageUploadResponse
        {
            ImageUrl = result.ImageUrl,
            FileName = result.FileName,
            UploadedAt = DateTime.UtcNow
        };

        return Ok(response);
    }

    private async Task<UserContext> GetUserContextAsync(int clubId)
    {
        var userId = User.GetUserId();
        if (userId is null) return new(Unauthorized(new { message = "\u767b\u5f55\u72b6\u6001\u5df2\u5931\u6548\u3002" }), null, null);
        var user = await _db.Users.FindAsync(userId.Value);
        if (user is null) return new(Unauthorized(new { message = "\u767b\u5f55\u72b6\u6001\u5df2\u5931\u6548\u3002" }), null, null);
        if (!UsersController.IsActive(user.AccountStatus)) return new(Unauthorized(new { message = "\u5f53\u524d\u8d26\u53f7\u4e0d\u53ef\u7528\u3002" }), null, null);
        if (!await _db.Clubs.AnyAsync(club => club.ClubId == clubId)) return new(NotFound(new { message = "\u793e\u56e2\u4e0d\u5b58\u5728\u3002" }), null, null);
        var roles = await _authService.GetPermissionRolesAsync(user.UserId);
        var canModerate = Allows(roles, ForumModeratePermission, clubId);
        if (!canModerate && !await _projectMembershipService.IsActiveClubMemberAsync(clubId, user.UserId)) return new(StatusCode(403, new { message = "\u53ea\u6709\u5f53\u524d\u6709\u6548\u6210\u5458\u53ef\u8bbf\u95ee\u3002" }), null, null);
        if (!Allows(roles, ClubViewPermission, clubId) && !Allows(roles, ForumPostPermission, clubId) && !canModerate) return new(StatusCode(403, new { message = "\u6ca1\u6709\u8bbf\u95ee\u6743\u9650\u3002" }), null, null);
        return new(null, user, roles);
    }

    private static bool Allows(IReadOnlyList<PermissionRole> roles, string permission, int clubId) =>
        AuthService.RolesAllow(roles, permission, clubId);
    private static bool IsPublished(ForumPost post) => string.Equals(post.PostStatus, Published, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(post.PostStatus);

    private static List<ApiForumPost> BuildNestedReplies(List<ForumPost> allPosts, int? parentPostId, bool includeHidden)
    {
        var directReplies = allPosts.Where(r => r.ParentPostId == parentPostId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.PostId).ToList();
        var result = new List<ApiForumPost>();
        foreach (var reply in directReplies)
        {
            if (!includeHidden && !IsPublished(reply)) continue;
            var nestedReplies = BuildNestedReplies(allPosts, reply.PostId, includeHidden);
            result.Add(ToApiPost(reply, nestedReplies));
        }
        return result;
    }

    private static ApiForumPost ToApiPost(ForumPost post, List<ApiForumPost> replies) => new() { Id = post.PostId, ClubId = post.ClubId, UserId = post.UserId, UserName = post.User is null ? null : (string.IsNullOrWhiteSpace(post.User.RealName) ? post.User.Username : post.User.RealName), ParentPostId = post.ParentPostId, Title = post.Title, Content = post.Content, IsTop = post.IsTop != 0, PostStatus = IsPublished(post) ? ApiForumPost.PostStatusEnum.PublishedEnum : ApiForumPost.PostStatusEnum.HiddenEnum, CreatedAt = LearningWorkflow.AsUtc(post.CreatedAt), UpdatedAt = LearningWorkflow.AsUtc(post.UpdatedAt), Replies = replies };
    private sealed record UserContext(IActionResult? Result, User? User, IReadOnlyList<PermissionRole>? Roles);
}
