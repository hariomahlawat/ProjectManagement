using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Tests;

public sealed class ConferenceRemarkFoundationTests
{
    [Fact]
    public void ConferenceRemarkPolicy_IsRestrictedToComdtAndHoD()
    {
        Assert.Equal("ConferenceRemarks.Manage", Policies.ConferenceRemarks.Manage);
        Assert.Equal(
            new[] { RoleNames.Comdt, RoleNames.HoD },
            Policies.ConferenceRemarks.ManageAllowedRoles);
    }

    [Fact]
    public void SourceRemarkModels_ExposeConferenceAsANativeType()
    {
        Assert.Equal(2, (int)RemarkType.Conference);
        Assert.Contains(ProjectIdeaCommentTypes.Conference, ProjectIdeaCommentTypes.All);
        Assert.Contains(ActionTaskUpdateTypes.Conference, ActionTaskUpdateTypes.All);
        Assert.Equal(ProjectIdeaCommentTypes.General, new ProjectIdeaComment().CommentType);
    }
}
