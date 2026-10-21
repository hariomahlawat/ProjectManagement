using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

// A returned document request is resolved only by a newer request for the same item.
public sealed class ReturnedDocumentIdentityTests
{
    [Fact]
    public void Upload_IsTheSameItem_ForTheSameStageAndTitle_IgnoringCaseAndSpacing()
        => Assert.Equal(
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Upload, null, 4, "AON Statement"),
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Upload, null, 4, "  aon statement "));

    [Theory]
    [InlineData(ProjectDocumentRequestType.Upload, null, 4, "TEC report")]
    [InlineData(ProjectDocumentRequestType.Upload, null, 5, "AON Statement")]
    [InlineData(ProjectDocumentRequestType.Replace, 9, 4, "AON Statement")]
    public void UnrelatedRequests_DoNotResolveAReturnedUpload(ProjectDocumentRequestType type, int? documentId, int? stageId, string title)
        => Assert.NotEqual(
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Upload, null, 4, "AON Statement"),
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(type, documentId, stageId, title));

    [Fact]
    public void ReplaceAndDelete_AreScopedToTheirDocument()
    {
        Assert.Equal(
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Replace, 9, 1, "Old title"),
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Replace, 9, 2, "New title"));
        Assert.NotEqual(
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Replace, 9, 1, "x"),
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Replace, 10, 1, "x"));
        Assert.NotEqual(
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Replace, 9, 1, "x"),
            ProjectOfficerWorkspaceService.DocumentRequestIdentity(ProjectDocumentRequestType.Delete, 9, 1, "x"));
    }
}
