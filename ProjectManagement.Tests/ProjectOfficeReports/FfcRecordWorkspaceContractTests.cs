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
        var projectEditor = ReadRecord("_ProjectEditor.cshtml");
        var workspaceProjects = ReadRecord("_WorkspaceProjects.cshtml");
        var attachmentEditor = ReadRecord("_AttachmentUploader.cshtml");

        Assert.Contains("_WorkspaceSummary.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("_WorkspaceProjects.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("_WorkspaceAttachments.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("ffc-project-editor-data", details, StringComparison.Ordinal);
        Assert.Contains("_PrismConfirmDialog.cshtml", details, StringComparison.Ordinal);
        Assert.Contains("data-bs-backdrop=\"static\"", projectEditor, StringComparison.Ordinal);
        Assert.Contains("data-bs-keyboard=\"false\"", projectEditor, StringComparison.Ordinal);
        Assert.Contains("same canonical external remark", projectEditor, StringComparison.Ordinal);
        Assert.Contains("DeliveredAwaitingInstallation", projectEditor, StringComparison.Ordinal);
        Assert.Contains("Model.CanManage || project.LinkedProjectId.HasValue", workspaceProjects, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project-progress", workspaceProjects, StringComparison.Ordinal);
        Assert.Contains("data-ffc-upload-zone", attachmentEditor, StringComparison.Ordinal);
        Assert.Contains("data-ffc-upload-submit", attachmentEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("FfcAttachmentKind", attachmentEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceSummary_UsesAccurateMilestoneDateSemanticsAndContextualEmptyActions()
    {
        var summary = ReadRecord("_WorkspaceSummary.cshtml");
        var projects = ReadRecord("_WorkspaceProjects.cshtml");

        Assert.Contains("Model.Workspace.Ipa.IsCompleted", summary, StringComparison.Ordinal);
        Assert.Contains("Model.Workspace.Gsl.IsCompleted", summary, StringComparison.Ordinal);
        Assert.Contains("Completion date missing", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("No date recorded", summary, StringComparison.Ordinal);
        Assert.Contains("Model.CanManage && Model.Workspace.Projects.Count > 0", projects, StringComparison.Ordinal);
        Assert.Contains("Quantity status", summary, StringComparison.Ordinal);
        Assert.Contains("Overall status", summary, StringComparison.Ordinal);
        Assert.Contains("role=\"columnheader\">Status", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Unit position", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Overall position", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceScript_UsesStyledConfirmationAndReliableInteractionContracts()
    {
        var script = ReadRecord("ffc-record-workspace.js");

        Assert.Contains("data-ffc-project-picker", script, StringComparison.Ordinal);
        Assert.Contains("Discard unsaved changes?", script, StringComparison.Ordinal);
        Assert.Contains("beforeunload", script, StringComparison.Ordinal);
        Assert.Contains("data-ffc-delete-project", script, StringComparison.Ordinal);
        Assert.Contains("data-ffc-delete-attachment", script, StringComparison.Ordinal);
        Assert.Contains("DataTransfer", script, StringComparison.Ordinal);
        Assert.Contains("value.name && value.size === 0", script, StringComparison.Ordinal);
        Assert.Contains("ffcSubmitLocked", script, StringComparison.Ordinal);
        Assert.Contains("setCustomValidity", script, StringComparison.Ordinal);
        Assert.Contains("ffc-project-editor-data", script, StringComparison.Ordinal);
        Assert.Contains("focusValidationErrors", script, StringComparison.Ordinal);
        Assert.Contains("forceDirty: false", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmationComponent_IsReusableAccessibleAndPromiseBased()
    {
        var partial = ReadShared("_PrismConfirmDialog.cshtml");
        var script = ReadShared("prism-confirm-dialog.js");
        var css = ReadShared("prism-confirm-dialog.css");

        Assert.Contains("<dialog", partial, StringComparison.Ordinal);
        Assert.Contains("data-prism-confirm-accept", partial, StringComparison.Ordinal);
        Assert.Contains("window.PrismConfirm", script, StringComparison.Ordinal);
        Assert.Contains("return new Promise", script, StringComparison.Ordinal);
        Assert.Contains("::backdrop", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchivedRecords_UseStyledRestoreConfirmation()
    {
        var archived = ReadRecord("Archived.cshtml");
        var script = ReadRecord("ffc-record-workspace.js");

        Assert.Contains("asp-page-handler=\"Restore\"", archived, StringComparison.Ordinal);
        Assert.Contains("data-ffc-restore-record", archived, StringComparison.Ordinal);
        Assert.Contains("Restore this FFC record?", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", script, StringComparison.Ordinal);
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

    private static string ReadShared(string fileName)
        => Read(Path.Combine("TestData", "Ffc", "Shared", fileName));

    private static string Read(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(path), $"FFC workspace contract file was not copied to the test output: {path}");
        return File.ReadAllText(path);
    }
}
