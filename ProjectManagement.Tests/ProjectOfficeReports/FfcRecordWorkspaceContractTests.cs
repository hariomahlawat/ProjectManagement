using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcRecordWorkspaceContractTests
{
    [Fact]
    public void Portfolio_NavigatesToUnifiedWorkspaceAndCreatePage()
    {
        var overview = ReadFfc("Index.cshtml");
        var row = ReadFfc("_PortfolioRow.cshtml");

        Assert.Contains("/FFC/Records/Create", overview, StringComparison.Ordinal);
        Assert.Contains("/FFC/Records/Details", row, StringComparison.Ordinal);
        Assert.DoesNotContain("/FFC/Records/Projects/Manage", row, StringComparison.Ordinal);
        Assert.DoesNotContain("/FFC/Records/Attachments/Upload", row, StringComparison.Ordinal);
        Assert.Contains("asp-fragment=\"projects\"", row, StringComparison.Ordinal);
        Assert.Contains("asp-fragment=\"attachments\"", row, StringComparison.Ordinal);
        Assert.Contains("/FFC/Records/Archived", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_ConsolidatesSummaryProjectsAttachmentsAndProtectedEditors()
    {
        var details = ReadRecord("Details.cshtml");
        var recordEditor = ReadRecord("_RecordEditor.cshtml");
        var projectEditor = ReadRecord("_ProjectEditor.cshtml");
        var workspaceProjects = ReadRecord("_WorkspaceProjects.cshtml");
        var attachmentEditor = ReadRecord("_AttachmentUploader.cshtml");

        Assert.Contains("_WorkspaceSummary.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("_WorkspaceProjects.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("_WorkspaceAttachments.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("data-bs-backdrop=\"static\"", projectEditor, StringComparison.Ordinal);
        Assert.Contains("data-bs-keyboard=\"false\"", projectEditor, StringComparison.Ordinal);
        Assert.Contains("Shared with the linked Project external remark", projectEditor, StringComparison.Ordinal);
        Assert.Contains("DeliveredAwaitingInstallation", projectEditor, StringComparison.Ordinal);
        Assert.Contains("Model.CanManage || project.LinkedProjectId.HasValue", workspaceProjects, StringComparison.Ordinal);
        Assert.Contains("data-ffc-upload-zone", attachmentEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("FfcAttachmentKind", attachmentEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceScript_ProvidesSearchDirtyFormAndDeleteConfirmationContracts()
    {
        var script = ReadRecord("ffc-record-workspace.js");

        Assert.Contains("data-ffc-project-picker", script, StringComparison.Ordinal);
        Assert.Contains("Discard unsaved changes?", script, StringComparison.Ordinal);
        Assert.Contains("beforeunload", script, StringComparison.Ordinal);
        Assert.Contains("data-ffc-delete-project", script, StringComparison.Ordinal);
        Assert.Contains("data-ffc-delete-attachment", script, StringComparison.Ordinal);
        Assert.Contains("DataTransfer", script, StringComparison.Ordinal);
        Assert.Contains("Changing the position will clear", script, StringComparison.Ordinal);
        Assert.Contains("Never carry one project's", script, StringComparison.Ordinal);
    }


    [Fact]
    public void ArchivedRecords_ProvideAnExplicitRestorePath()
    {
        var archived = ReadRecord("Archived.cshtml");
        var script = ReadRecord("ffc-record-workspace.js");

        Assert.Contains("asp-page-handler=\"Restore\"", archived, StringComparison.Ordinal);
        Assert.Contains("data-ffc-restore-record", archived, StringComparison.Ordinal);
        Assert.Contains("Restore this FFC record to the active portfolio?", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceStyles_AreScopedAndDoNotTargetProtectedDetailedTable()
    {
        var css = ReadRecord("ffc-record-workspace.css");

        Assert.Contains(".ffc-workspace", css, StringComparison.Ordinal);
        Assert.DoesNotContain("ffc-dtable", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("map-table-detailed", css, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadFfc(string fileName)
        => Read(Path.Combine("TestData", "Ffc", fileName));

    private static string ReadRecord(string fileName)
        => Read(Path.Combine("TestData", "Ffc", "Records", fileName));

    private static string Read(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(path), $"FFC workspace contract file was not copied to the test output: {path}");
        return File.ReadAllText(path);
    }
}
