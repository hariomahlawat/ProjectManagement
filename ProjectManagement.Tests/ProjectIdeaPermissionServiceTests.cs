using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Tests;

public class ProjectIdeaPermissionServiceTests
{
    private readonly ProjectIdeaPermissionService _service = new();

    [Fact]
    public void Assigned_project_officer_can_edit_description_but_not_core_fields()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.True(_service.CanEditDescription(user, idea));
        Assert.True(_service.CanEditIdea(user, idea));
        Assert.False(_service.CanEditIdeaCore(user, idea));
    }

    [Fact]
    public void Unassigned_project_officer_cannot_edit_description()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-2");
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.False(_service.CanEditDescription(user, idea));
        Assert.False(_service.CanEditIdea(user, idea));
    }

    [Fact]
    public void Privileged_user_can_edit_description_and_core_fields()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        var user = Principal("hod-1", RoleNames.HoD);

        Assert.True(_service.CanEditDescription(user, idea));
        Assert.True(_service.CanEditIdeaCore(user, idea));
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

    [Fact]
    public void Archived_idea_is_read_only_for_assigned_project_officer()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        idea.Status = ProjectIdeaStatuses.Archived;
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.False(_service.CanEditDescription(user, idea));
        Assert.False(_service.CanEditIdea(user, idea));
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

    [Theory]
    [InlineData(RoleNames.Comdt, true)]
    [InlineData(RoleNames.HoD, true)]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.ProjectOfficer, false)]
    public void Idea_delete_and_deleted_restore_are_command_governed(string role, bool expected)
    {
        var user = Principal($"user-{role}", role);

        Assert.Equal(expected, _service.CanDeleteIdea(user));
        Assert.Equal(expected, _service.CanRestoreDeletedIdea(user));
        Assert.Equal(expected, _service.CanViewDeletedIdeas(user));
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
            CreatedByUserId = "po-1",
            CreatedAt = now.AddHours(-2)
        };
        var user = Principal("po-1", RoleNames.ProjectOfficer);

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
