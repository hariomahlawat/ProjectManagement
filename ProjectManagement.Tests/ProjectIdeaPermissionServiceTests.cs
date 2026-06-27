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

    [Fact]
    public void Archived_idea_is_read_only_for_assigned_project_officer()
    {
        var idea = ActiveIdea(assignedProjectOfficerUserId: "po-1");
        idea.Status = ProjectIdeaStatuses.Archived;
        var user = Principal("po-1", RoleNames.ProjectOfficer);

        Assert.False(_service.CanEditDescription(user, idea));
        Assert.False(_service.CanEditIdea(user, idea));
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

    private static ClaimsPrincipal Principal(string userId, string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            },
            authenticationType: "Test");

        return new ClaimsPrincipal(identity);
    }
}
