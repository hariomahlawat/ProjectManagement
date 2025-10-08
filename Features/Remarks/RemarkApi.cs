using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Features.Remarks;

internal static class RemarkApi
{
    private const string AllowedRoles = "Admin,HoD,Project Officer,Comdt,MCO,Project Office,Main Office,TA";

    public static void MapRemarkApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:int}/remarks")
            .RequireAuthorization(new AuthorizeAttribute { Roles = AllowedRoles });

        group.MapPost("", CreateRemarkAsync);
        group.MapGet("", ListRemarksAsync);
        group.MapPut("/{remarkId:int}", UpdateRemarkAsync);
        group.MapDelete("/{remarkId:int}", DeleteRemarkAsync);
        group.MapGet("/{remarkId:int}/audit", GetRemarkAuditAsync);
    }

    private static async Task<IResult> CreateRemarkAsync(
        int projectId,
        [FromBody] CreateRemarkRequestDto request,
        ApplicationDbContext db,
        IRemarkService remarkService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        request ??= new CreateRemarkRequestDto();

        var (actor, error) = await BuildActorContextAsync(
            userManager,
            httpContext,
            request.ActorRole,
            projectId,
            db,
            cancellationToken);
        if (error is IResult errorResult)
        {
            return errorResult;
        }

        try
        {
            var remark = await remarkService.CreateRemarkAsync(
                new CreateRemarkRequest(
                    ProjectId: projectId,
                    Actor: actor!,
                    Type: request.Type,
                    Scope: request.Scope,
                    Body: request.Body ?? string.Empty,
                    EventDate: request.EventDate,
                    StageRef: request.StageRef,
                    StageNameSnapshot: request.StageName,
                    Meta: request.Meta),
                cancellationToken);

            var authorUser = await userManager.FindByIdAsync(remark.AuthorUserId);
            var mentionMap = await LoadUserInfoAsync(remark.Mentions.Select(m => m.UserId), db, cancellationToken);
            var mentionDtos = BuildMentionDtos(remark, mentionMap);
            var response = ToDto(remark, BuildUserInfo(authorUser, remark.AuthorUserId), null, mentionDtos);
            return Results.Created($"/api/projects/{projectId}/remarks/{remark.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> ListRemarksAsync(
        int projectId,
        ApplicationDbContext db,
        IRemarkService remarkService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string? type,
        [FromQuery] string? scope,
        [FromQuery] string? role,
        [FromQuery] string? stageRef,
        [FromQuery(Name = "mine")] bool? mine,
        [FromQuery(Name = "dateFrom")] DateOnly? from,
        [FromQuery(Name = "dateTo")] DateOnly? to,
        [FromQuery(Name = "includeDeleted")] bool? includeDeleted,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery(Name = "actorRole")] string? actorRole)
    {
        var (actor, error) = await BuildActorContextAsync(
            userManager,
            httpContext,
            actorRole,
            projectId,
            db,
            cancellationToken,
            allowViewerFallback: true);
        if (error is IResult errorResult)
        {
            return errorResult;
        }

        if (!TryParseRemarkType(type, out var remarkType, out var typeError))
        {
            return typeError!;
        }

        if (!TryParseRemarkScope(scope, out var remarkScope, out var scopeError))
        {
            return scopeError!;
        }

        if (!TryParseRemarkRole(role, out var authorRole, out var roleError))
        {
            return roleError!;
        }

        try
        {
            var pageValue = page.GetValueOrDefault();
            var pageSizeValue = pageSize.GetValueOrDefault();

            var result = await remarkService.ListRemarksAsync(
                new ListRemarksRequest(
                    ProjectId: projectId,
                    Actor: actor!,
                    Type: remarkType,
                    Scope: remarkScope,
                    AuthorRole: authorRole,
                    StageRef: stageRef,
                    FromDate: from,
                    ToDate: to,
                    Mine: mine ?? false,
                    IncludeDeleted: includeDeleted ?? false,
                    Page: pageValue <= 0 ? 1 : pageValue,
                    PageSize: pageSizeValue <= 0 ? 20 : pageSizeValue),
                cancellationToken);

            var userIds = result.Items
                .Select(r => r.AuthorUserId)
                .Concat(result.Items
                    .Where(r => !string.IsNullOrWhiteSpace(r.DeletedByUserId))
                    .Select(r => r.DeletedByUserId!))
                .Concat(result.Items
                    .SelectMany(r => r.Mentions)
                    .Select(m => m.UserId));

            var userMap = await LoadUserInfoAsync(userIds, db, cancellationToken);

            var items = result.Items.Select(remark =>
            {
                userMap.TryGetValue(remark.AuthorUserId, out var authorInfo);
                RemarkUserInfo? deleterInfo = null;
                if (!string.IsNullOrWhiteSpace(remark.DeletedByUserId) && userMap.TryGetValue(remark.DeletedByUserId!, out var info))
                {
                    deleterInfo = info;
                }

                var mentionDtos = BuildMentionDtos(remark, userMap);
                return ToDto(remark, authorInfo, deleterInfo, mentionDtos);
            }).ToArray();

            var response = new RemarkListResponse(
                result.TotalCount,
                result.Page,
                result.PageSize,
                items);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> UpdateRemarkAsync(
        int projectId,
        int remarkId,
        [FromBody] UpdateRemarkRequestDto request,
        ApplicationDbContext db,
        IRemarkService remarkService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        request ??= new UpdateRemarkRequestDto();

        if (!await db.Remarks.AsNoTracking().AnyAsync(r => r.Id == remarkId && r.ProjectId == projectId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (!TryParseRowVersion(request.RowVersion, out var rowVersion, out var rowError))
        {
            return rowError!;
        }

        var (actor, error) = await BuildActorContextAsync(
            userManager,
            httpContext,
            request.ActorRole,
            projectId,
            db,
            cancellationToken);
        if (error is IResult errorResult)
        {
            return errorResult;
        }

        try
        {
            var remark = await remarkService.EditRemarkAsync(
                remarkId,
                new EditRemarkRequest(
                    Actor: actor!,
                    Body: request.Body ?? string.Empty,
                    Scope: request.Scope,
                    EventDate: request.EventDate,
                    StageRef: request.StageRef,
                    StageNameSnapshot: request.StageName,
                    Meta: request.Meta,
                    RowVersion: rowVersion),
                cancellationToken);

            if (remark is null || remark.ProjectId != projectId)
            {
                return Results.NotFound();
            }

            var authorUser = await userManager.FindByIdAsync(remark.AuthorUserId);
            var mentionMap = await LoadUserInfoAsync(remark.Mentions.Select(m => m.UserId), db, cancellationToken);
            var mentionDtos = BuildMentionDtos(remark, mentionMap);
            return Results.Ok(ToDto(remark, BuildUserInfo(authorUser, remark.AuthorUserId), null, mentionDtos));
        }
        catch (InvalidOperationException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> DeleteRemarkAsync(
        int projectId,
        int remarkId,
        [FromBody] DeleteRemarkRequestDto request,
        ApplicationDbContext db,
        IRemarkService remarkService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        request ??= new DeleteRemarkRequestDto();

        if (!await db.Remarks.AsNoTracking().AnyAsync(r => r.Id == remarkId && r.ProjectId == projectId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (!TryParseRowVersion(request.RowVersion, out var rowVersion, out var rowError))
        {
            return rowError!;
        }

        var (actor, error) = await BuildActorContextAsync(
            userManager,
            httpContext,
            request.ActorRole,
            projectId,
            db,
            cancellationToken);
        if (error is IResult errorResult)
        {
            return errorResult;
        }

        try
        {
            var deleted = await remarkService.SoftDeleteRemarkAsync(
                remarkId,
                new SoftDeleteRemarkRequest(
                    Actor: actor!,
                    Meta: request.Meta,
                    RowVersion: rowVersion),
                cancellationToken);

            if (!deleted)
            {
                return Results.NotFound();
            }

            var remark = await db.Remarks.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == remarkId, cancellationToken);

            var response = new DeleteRemarkResponse(
                Success: true,
                RowVersion: remark?.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> GetRemarkAuditAsync(
        int projectId,
        int remarkId,
        ApplicationDbContext db,
        IRemarkService remarkService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery(Name = "actorRole")] string? actorRole)
    {
        if (!await db.Remarks.AsNoTracking().AnyAsync(r => r.Id == remarkId && r.ProjectId == projectId, cancellationToken))
        {
            return Results.NotFound();
        }

        var (actor, error) = await BuildActorContextAsync(
            userManager,
            httpContext,
            actorRole,
            projectId,
            db,
            cancellationToken,
            allowViewerFallback: true);
        if (error is IResult errorResult)
        {
            return errorResult;
        }

        if (!actor!.Roles.Contains(RemarkActorRole.Administrator))
        {
            return ForbiddenProblem(RemarkService.PermissionDeniedMessage);
        }

        try
        {
            var audits = await remarkService.GetRemarkAuditAsync(remarkId, actor, cancellationToken);
            var response = audits
                .Select(a => new RemarkAuditDto(
                    a.Id,
                    a.Action,
                    a.ActorRole,
                    a.ActorUserId ?? string.Empty,
                    ToUtcDateTimeOffset(a.ActionAtUtc),
                    a.Meta,
                    new RemarkSnapshotDto(
                        a.SnapshotType,
                        a.SnapshotScope,
                        a.SnapshotAuthorRole,
                        a.SnapshotAuthorUserId,
                        a.SnapshotBody,
                        a.SnapshotEventDate,
                        a.SnapshotStageRef,
                        a.SnapshotStageName,
                        ToUtcDateTimeOffset(a.SnapshotCreatedAtUtc),
                        ToUtcDateTimeOffset(a.SnapshotLastEditedAtUtc),
                        a.SnapshotIsDeleted,
                        ToUtcDateTimeOffset(a.SnapshotDeletedAtUtc),
                        a.SnapshotDeletedByUserId,
                        a.SnapshotDeletedByRole)))
                .ToArray();

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static RemarkResponseDto ToDto(
        Remark remark,
        RemarkUserInfo? author = null,
        RemarkUserInfo? deleter = null,
        IReadOnlyList<RemarkMentionDto>? mentions = null)
    {
        var createdAt = ToUtcDateTimeOffset(remark.CreatedAtUtc);
        var lastEditedAt = ToUtcDateTimeOffset(remark.LastEditedAtUtc);
        var deletedAt = ToUtcDateTimeOffset(remark.DeletedAtUtc);

        return new RemarkResponseDto(
            remark.Id,
            remark.ProjectId,
            remark.Type,
            remark.Scope,
            remark.AuthorRole,
            remark.AuthorUserId,
            author?.DisplayName ?? remark.AuthorUserId,
            author?.Initials ?? BuildInitials(remark.AuthorUserId),
            remark.Body,
            remark.EventDate,
            remark.StageRef,
            remark.StageNameSnapshot,
            createdAt,
            lastEditedAt,
            remark.IsDeleted,
            deletedAt,
            remark.DeletedByUserId,
            remark.DeletedByRole,
            deleter?.DisplayName,
            remark.RowVersion is { Length: > 0 } rowVersion ? Convert.ToBase64String(rowVersion) : string.Empty,
            mentions ?? Array.Empty<RemarkMentionDto>());
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
    {
        if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            value = value.ToUniversalTime();
        }

        return new DateTimeOffset(value, TimeSpan.Zero);
    }

    private static DateTimeOffset? ToUtcDateTimeOffset(DateTime? value)
        => value.HasValue ? ToUtcDateTimeOffset(value.Value) : null;

    private static async Task<(RemarkActorContext? Actor, IResult? Error)> BuildActorContextAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        string? requestedRole,
        int projectId,
        ApplicationDbContext db,
        CancellationToken cancellationToken,
        bool allowViewerFallback = false)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
        {
            return (null, Results.Unauthorized());
        }

        var assignedRoles = await userManager.GetRolesAsync(user);
        var remarkRoleSet = assignedRoles
            .Select(r => RemarkActorRoleExtensions.TryParse(r, out var parsed) ? parsed : RemarkActorRole.Unknown)
            .Where(r => r != RemarkActorRole.Unknown)
            .ToHashSet();

        Project? project = null;
        if (remarkRoleSet.Count == 0)
        {
            project = await db.Projects.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);

            if (project is not null)
            {
                if (!string.IsNullOrWhiteSpace(project.LeadPoUserId)
                    && string.Equals(project.LeadPoUserId, user.Id, StringComparison.Ordinal))
                {
                    remarkRoleSet.Add(RemarkActorRole.ProjectOfficer);
                }

                if (!string.IsNullOrWhiteSpace(project.HodUserId)
                    && string.Equals(project.HodUserId, user.Id, StringComparison.Ordinal))
                {
                    remarkRoleSet.Add(RemarkActorRole.HeadOfDepartment);
                }
            }

            if (remarkRoleSet.Count == 0)
            {
                if (allowViewerFallback
                    && project is not null
                    && ProjectAccessGuard.CanViewProject(project, httpContext.User, user.Id))
                {
                    var fallbackRole = RemarkActorRole.ProjectOfficer;
                    return (new RemarkActorContext(
                        user.Id,
                        fallbackRole,
                        new[] { fallbackRole },
                        true), null);
                }

                return (null, ForbiddenProblem(RemarkService.PermissionDeniedMessage));
            }
        }

        var remarkRoles = remarkRoleSet.ToArray();
        RemarkActorRole? desiredRole = null;
        if (!string.IsNullOrWhiteSpace(requestedRole))
        {
            if (!RemarkActorRoleExtensions.TryParse(requestedRole, out var parsed) || parsed == RemarkActorRole.Unknown)
            {
                return (null, Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid actor role.",
                    Detail = "Actor role is not recognised."
                }));
            }

            if (!remarkRoles.Contains(parsed))
            {
                return (null, ForbiddenProblem(RemarkService.PermissionDeniedMessage));
            }

            desiredRole = parsed;
        }

        var selected = desiredRole ?? SelectDefaultRole(remarkRoles);
        if (selected == RemarkActorRole.Unknown)
        {
            return (null, ForbiddenProblem(RemarkService.PermissionDeniedMessage));
        }

        return (new RemarkActorContext(user.Id, selected, remarkRoles), null);
    }

    private static RemarkActorRole SelectDefaultRole(IReadOnlyCollection<RemarkActorRole> roles)
    {
        foreach (var candidate in new[]
                 {
                     RemarkActorRole.ProjectOfficer,
                     RemarkActorRole.HeadOfDepartment,
                     RemarkActorRole.Commandant,
                     RemarkActorRole.Administrator,
                     RemarkActorRole.ProjectOffice,
                     RemarkActorRole.MainOffice,
                     RemarkActorRole.Mco,
                     RemarkActorRole.Ta
                 })
        {
            if (roles.Contains(candidate))
            {
                return candidate;
            }
        }

        return RemarkActorRole.Unknown;
    }

    private static bool TryParseRemarkType(string? value, out RemarkType? type, out IResult? error)
    {
        type = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<RemarkType>(value, true, out var parsed))
        {
            type = parsed;
            return true;
        }

        error = Results.BadRequest(new ProblemDetails
        {
            Title = "Invalid type.",
            Detail = "Type must be 'Internal' or 'External'."
        });
        return false;
    }

    private static bool TryParseRemarkRole(string? value, out RemarkActorRole? role, out IResult? error)
    {
        role = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (RemarkActorRoleExtensions.TryParse(value, out var parsed) && parsed != RemarkActorRole.Unknown)
        {
            role = parsed;
            return true;
        }

        error = Results.BadRequest(new ProblemDetails
        {
            Title = "Invalid role filter.",
            Detail = "Role is not recognised."
        });
        return false;
    }

    private static bool TryParseRemarkScope(string? value, out RemarkScope? scope, out IResult? error)
    {
        scope = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = trimmed
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (string.Equals(normalized, "tot", StringComparison.OrdinalIgnoreCase))
        {
            normalized = RemarkScope.TransferOfTechnology.ToString();
        }

        if (Enum.TryParse<RemarkScope>(normalized, true, out var parsed))
        {
            scope = parsed;
            return true;
        }

        error = Results.BadRequest(new ProblemDetails
        {
            Title = "Invalid scope.",
            Detail = "Scope must be 'General' or 'TransferOfTechnology'."
        });
        return false;
    }

    private static bool TryParseRowVersion(string? value, out byte[] rowVersion, out IResult? error)
    {
        rowVersion = Array.Empty<byte>();
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = Results.BadRequest(new ProblemDetails
            {
                Title = "Row version required.",
                Detail = "Row version must be supplied as a base64 string."
            });
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            if (rowVersion.Length == 0)
            {
                error = Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid row version.",
                    Detail = "Row version must not be empty."
                });
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            error = Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid row version.",
                Detail = "Row version must be base64 encoded."
            });
            return false;
        }
    }

    private static IResult MapServiceException(InvalidOperationException ex)
        => ex.Message switch
        {
            "Project not found." => Results.NotFound(new ProblemDetails { Title = "Project not found.", Detail = ex.Message }),
            RemarkService.ConcurrencyConflictMessage => Results.Conflict(new ProblemDetails { Title = ex.Message, Detail = ex.Message }),
            RemarkService.RowVersionRequiredMessage => Results.BadRequest(new ProblemDetails { Title = "Row version required.", Detail = ex.Message }),
            RemarkService.PermissionDeniedMessage => ForbiddenProblem(ex.Message),
            RemarkService.EditWindowMessage => ForbiddenProblem(ex.Message),
            RemarkService.DeleteWindowMessage => ForbiddenProblem(ex.Message),
            RemarkService.StageNotInProjectMessage => Results.BadRequest(new ProblemDetails { Title = ex.Message, Detail = ex.Message }),
            "Only administrators may view remark audits." => ForbiddenProblem(RemarkService.PermissionDeniedMessage),
            "Actor role is not recognised or not assigned." => ForbiddenProblem(RemarkService.PermissionDeniedMessage),
            "Actor role is not recognised or not granted to the user." => ForbiddenProblem(RemarkService.PermissionDeniedMessage),
            "External remarks require HoD, Comdt or Admin role." => ForbiddenProblem(RemarkService.PermissionDeniedMessage),
            _ => Results.BadRequest(new ProblemDetails { Title = "Remark request failed.", Detail = ex.Message })
        };

    private static IResult ForbiddenProblem(string message)
        => Results.Problem(message, statusCode: StatusCodes.Status403Forbidden, title: message);

    private static async Task<Dictionary<string, RemarkUserInfo>> LoadUserInfoAsync(
        IEnumerable<string> userIds,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<string, RemarkUserInfo>(StringComparer.Ordinal);
        }

        var users = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName, u.Email })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, RemarkUserInfo>(StringComparer.Ordinal);
        foreach (var user in users)
        {
            var displayName = ResolveDisplayName(user.FullName, user.UserName, user.Email, user.Id);
            result[user.Id] = new RemarkUserInfo(user.Id, displayName, BuildInitials(displayName));
        }

        return result;
    }

    private static IReadOnlyList<RemarkMentionDto> BuildMentionDtos(
        Remark remark,
        IReadOnlyDictionary<string, RemarkUserInfo>? userMap)
    {
        if (remark.Mentions is null || remark.Mentions.Count == 0)
        {
            return Array.Empty<RemarkMentionDto>();
        }

        var mentions = new List<RemarkMentionDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mention in remark.Mentions)
        {
            if (string.IsNullOrWhiteSpace(mention.UserId) || !seen.Add(mention.UserId))
            {
                continue;
            }

            if (userMap is not null && userMap.TryGetValue(mention.UserId, out var userInfo))
            {
                mentions.Add(new RemarkMentionDto(mention.UserId, userInfo.DisplayName, userInfo.Initials));
            }
            else
            {
                var displayName = mention.UserId;
                mentions.Add(new RemarkMentionDto(mention.UserId, displayName, BuildInitials(displayName)));
            }
        }

        return mentions;
    }

    private static RemarkUserInfo BuildUserInfo(ApplicationUser? user, string fallbackUserId)
    {
        var displayName = ResolveDisplayName(user?.FullName, user?.UserName, user?.Email, fallbackUserId);
        return new RemarkUserInfo(user?.Id ?? fallbackUserId, displayName, BuildInitials(displayName));
    }

    private static string ResolveDisplayName(string? fullName, string? userName, string? email, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName!;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName!;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email!;
        }

        return fallback;
    }

    private static string BuildInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var parts = name
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            var word = parts[0];
            if (word.Length >= 2)
            {
                return string.Concat(char.ToUpperInvariant(word[0]), char.ToUpperInvariant(word[1]));
            }

            return word.ToUpperInvariant();
        }

        var first = char.ToUpperInvariant(parts[0][0]);
        var last = char.ToUpperInvariant(parts[^1][0]);
        return string.Concat(first, last);
    }

    private sealed record CreateRemarkRequestDto
    {
        public RemarkType Type { get; init; }
        public RemarkScope Scope { get; init; } = RemarkScope.General;
        public string? Body { get; init; }
        public DateOnly EventDate { get; init; }
        public string? StageRef { get; init; }
        public string? StageName { get; init; }
        public string? Meta { get; init; }
        public string? ActorRole { get; init; }
    }

    private sealed record UpdateRemarkRequestDto
    {
        public string? Body { get; init; }
        public RemarkScope Scope { get; init; } = RemarkScope.General;
        public DateOnly EventDate { get; init; }
        public string? StageRef { get; init; }
        public string? StageName { get; init; }
        public string? Meta { get; init; }
        public string? RowVersion { get; init; }
        public string? ActorRole { get; init; }
    }

    private sealed record DeleteRemarkRequestDto
    {
        public string? RowVersion { get; init; }
        public string? Meta { get; init; }
        public string? ActorRole { get; init; }
    }

    private sealed record RemarkResponseDto(
        int Id,
        int ProjectId,
        RemarkType Type,
        RemarkScope Scope,
        RemarkActorRole AuthorRole,
        string AuthorUserId,
        string AuthorDisplayName,
        string AuthorInitials,
        string Body,
        DateOnly EventDate,
        string? StageRef,
        string? StageName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastEditedAtUtc,
        bool IsDeleted,
        DateTimeOffset? DeletedAtUtc,
        string? DeletedByUserId,
        RemarkActorRole? DeletedByRole,
        string? DeletedByDisplayName,
        string RowVersion,
        IReadOnlyList<RemarkMentionDto> Mentions);

    private sealed record RemarkListResponse(
        int Total,
        int Page,
        int PageSize,
        IReadOnlyList<RemarkResponseDto> Items);

    private sealed record DeleteRemarkResponse(bool Success, string? RowVersion);

    private sealed record RemarkAuditDto(
        int Id,
        RemarkAuditAction Action,
        RemarkActorRole ActorRole,
        string ActorUserId,
        DateTimeOffset ActionAtUtc,
        string? Meta,
        RemarkSnapshotDto Snapshot);

    private sealed record RemarkSnapshotDto(
        RemarkType Type,
        RemarkScope Scope,
        RemarkActorRole AuthorRole,
        string AuthorUserId,
        string Body,
        DateOnly EventDate,
        string? StageRef,
        string? StageName,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastEditedAtUtc,
        bool IsDeleted,
        DateTimeOffset? DeletedAtUtc,
        string? DeletedByUserId,
        RemarkActorRole? DeletedByRole);

    private sealed record RemarkUserInfo(string UserId, string DisplayName, string Initials);

    private sealed record RemarkMentionDto(string UserId, string DisplayName, string Initials);
}
