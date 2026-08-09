using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Tests;

public class ProjectIdeaPermissionServiceTests
{
    private readonly ProjectIdeaPermissionService _service = new();

    [Fact]
    public void Any_authenticated_user_can_view_any_non_deleted_idea()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-2");
        idea.CreatedByUserId = "creator-2";
        idea.AssignedHodUserId = "hod-2";

        var unrelatedUser = Principal("user-1", "User");

        Assert.True(_service.CanViewIdea(unrelatedUser, idea));
    }

    [Fact]
    public void Unauthenticated_user_cannot_view_idea()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        Assert.False(_service.CanViewIdea(new ClaimsPrincipal(new ClaimsIdentity()), idea));
    }

    [Fact]
    public void Deleted_idea_is_not_visible_through_operational_details()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        idea.IsDeleted = true;

        Assert.False(_service.CanViewIdea(Principal("user-1", "User"), idea));
    }

    [Fact]
    public void Assigned_project_officer_can_edit_the_full_operational_idea_record()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.True(_service.CanEditDescription(user, idea));
        Assert.True(_service.CanEditIdea(user, idea));
        Assert.True(_service.CanEditIdeaCore(user, idea));
    }

    [Fact]
    public void Unassigned_project_officer_cannot_edit_idea()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-2");
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.False(_service.CanEditDescription(user, idea));
        Assert.False(_service.CanEditIdea(user, idea));
        Assert.False(_service.CanEditIdeaCore(user, idea));
    }

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    public void Idea_operational_editing_uses_assigned_po_or_command_roles(string role, bool expected)
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "different-po");
        var user = Principal($"user-{role}", role);

        Assert.Equal(expected, _service.CanEditIdea(user, idea));
        Assert.Equal(expected, _service.CanEditIdeaCore(user, idea));
    }

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    public void Idea_lifecycle_actions_are_command_admin_governed(string role, bool expected)
    {
        var user = Principal($"user-{role}", role);

        Assert.Equal(expected, _service.CanArchiveIdea(user));
        Assert.Equal(expected, _service.CanRestoreIdea(user));
        Assert.Equal(expected, _service.CanDeleteIdea(user));
        Assert.Equal(expected, _service.CanRestoreDeletedIdea(user));
        Assert.Equal(expected, _service.CanViewDeletedIdeas(user));
    }

    [Fact]
    public void Assigned_project_officer_does_not_gain_lifecycle_authority_from_assignment()
    {
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.False(_service.CanArchiveIdea(user));
        Assert.False(_service.CanRestoreIdea(user));
        Assert.False(_service.CanDeleteIdea(user));
        Assert.False(_service.CanRestoreDeletedIdea(user));
    }

    [Fact]
    public void Archived_idea_is_read_only_even_for_an_operational_editor()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        idea.Status = ProjectIdeaStatuses.Archived;

        Assert.False(_service.CanEditIdea(Principal("po-1", RoleNames.ProjectOfficer), idea));
        Assert.False(_service.CanEditIdea(Principal("hod-1", RoleNames.HoD), idea));
        Assert.False(_service.CanEditIdea(Principal("comdt-1", RoleNames.Comdt), idea));
    }

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    public void Conference_comments_are_limited_to_command_roles(string role, bool expected)
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var userId = role == RoleNames.ProjectOfficer ? "po-1" : $"user-{role}";
        var user = Principal(userId, role);

        Assert.Equal(expected, _service.CanAddConferenceComment(user, idea));
    }

    [Fact]
    public void Archived_idea_rejects_conference_comments()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        idea.Status = ProjectIdeaStatuses.Archived;

        Assert.False(_service.CanAddConferenceComment(Principal("hod-1", RoleNames.HoD), idea));
    }

    [Theory]
    [InlineData(RoleNames.Comdt, ProjectIdeaCommentTypes.Conference)]
    [InlineData(RoleNames.HoD, ProjectIdeaCommentTypes.General)]
    [InlineData(RoleNames.Admin, ProjectIdeaCommentTypes.General)]
    [InlineData(RoleNames.ProjectOfficer, ProjectIdeaCommentTypes.General)]
    public void Discussion_default_is_conference_for_commandant_only(string role, string expected)
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var userId = role == RoleNames.ProjectOfficer ? "po-1" : $"user-{role}";

        Assert.Equal(expected, _service.GetDefaultCommentType(Principal(userId, role), idea));
    }

    [Fact]
    public void Discussion_default_is_conference_when_commandant_has_multiple_roles()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var user = Principal("command-user", RoleNames.HoD, RoleNames.Comdt);

        Assert.Equal(ProjectIdeaCommentTypes.Conference, _service.GetDefaultCommentType(user, idea));
    }

    [Fact]
    public void General_comment_author_can_edit_and_delete_within_three_hours_only()
    {
        var now = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc);
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var comment = new ProjectIdeaComment
        {
            ProjectIdeaId = idea.Id,
            CommentType = ProjectIdeaCommentTypes.General,
            CommentText = "Progress",
            CreatedByUserId = "user-1",
            CreatedAt = now.AddHours(-2)
        };
        var user = Principal("user-1", "User");

        Assert.True(_service.CanEditComment(user, idea, comment, now));
        Assert.True(_service.CanDeleteComment(user, idea, comment, now));
        Assert.False(_service.CanEditComment(user, idea, comment, now.AddHours(2)));
        Assert.False(_service.CanDeleteComment(user, idea, comment, now.AddHours(2)));
    }

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    public void Conference_comment_mutation_matches_project_remark_command_governance(string role, bool expected)
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var comment = new ProjectIdeaComment
        {
            ProjectIdeaId = idea.Id,
            CommentType = ProjectIdeaCommentTypes.Conference,
            CommentText = "Direction",
            CreatedByUserId = "hod-1",
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        var userId = role == RoleNames.ProjectOfficer ? "po-1" : $"user-{role}";
        var user = Principal(userId, role);

        Assert.Equal(expected, _service.CanEditComment(user, idea, comment));
        Assert.Equal(expected, _service.CanDeleteComment(user, idea, comment));
    }

    private static ProjectIdea ActiveIdea(string assignedProjectOfficerUserId) => new()
    {
        Id = 1,
        Title = "Idea",
        Description = "Description",
        Status = ProjectIdeaStatuses.Active,
        AssignedProjectOfficerUserId = assignedProjectOfficerUserId,
        CreatedByUserId = "creator"
    };

    private static ClaimsPrincipal Principal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");

        return new ClaimsPrincipal(identity);
    }
}
