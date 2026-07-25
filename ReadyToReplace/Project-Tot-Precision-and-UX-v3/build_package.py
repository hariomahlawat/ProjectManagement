from __future__ import annotations

import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PACKAGE_ROOT = Path(__file__).resolve().parent
OUT = PACKAGE_ROOT / "Generated"
TEMPLATES = PACKAGE_ROOT / "Templates"


def read(path: str, *, v2: bool = False) -> str:
    base = ROOT / "ReadyToReplace/Project-Tot-Header-v2" if v2 else ROOT
    return (base / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    target = OUT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content.replace("\r\n", "\n"), encoding="utf-8", newline="\n")


def copy_template(path: str, destination: str | None = None) -> None:
    src = TEMPLATES / path
    dst = OUT / (destination or path)
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def replace_between(text: str, start: str, end: str, replacement: str, label: str) -> str:
    a = text.find(start)
    b = text.find(end, a + len(start))
    if a < 0 or b < 0:
        raise RuntimeError(f"{label}: marker not found")
    return text[:a] + replacement + text[b:]


def build_models() -> None:
    text = read("Models/ProjectTot.cs")
    text = replace_once(text, "using System.ComponentModel.DataAnnotations;\n", "using System.ComponentModel.DataAnnotations;\nusing ProjectManagement.Utilities.PartialDates;\n", "ProjectTot using")
    text = replace_once(text, "        public DateOnly? StartedOn { get; set; }\n\n        public DateOnly? CompletedOn { get; set; }\n", "        public DateOnly? StartedOn { get; set; }\n\n        public PartialDatePrecision StartDatePrecision { get; set; } = PartialDatePrecision.None;\n\n        public DateOnly? CompletedOn { get; set; }\n\n        public PartialDatePrecision CompletionDatePrecision { get; set; } = PartialDatePrecision.None;\n", "ProjectTot precision fields")
    write("Models/ProjectTot.cs", text)

    text = read("Models/ProjectTotRequest.cs")
    text = replace_once(text, "using System.ComponentModel.DataAnnotations;\n", "using System.ComponentModel.DataAnnotations;\nusing ProjectManagement.Utilities.PartialDates;\n", "ProjectTotRequest using")
    text = replace_once(text, "        public DateOnly? ProposedStartedOn { get; set; }\n\n        public DateOnly? ProposedCompletedOn { get; set; }\n", "        public DateOnly? ProposedStartedOn { get; set; }\n\n        public PartialDatePrecision ProposedStartDatePrecision { get; set; } = PartialDatePrecision.None;\n\n        public DateOnly? ProposedCompletedOn { get; set; }\n\n        public PartialDatePrecision ProposedCompletionDatePrecision { get; set; } = PartialDatePrecision.None;\n", "ProjectTotRequest precision fields")
    write("Models/ProjectTotRequest.cs", text)


def build_service() -> None:
    text = read("Services/Projects/ProjectTotService.cs")
    guard_update = '''        if (project is null)\n        {\n            return ProjectTotUpdateResult.NotFound();\n        }\n\n        if (project.IsDeleted || project.IsArchived || project.LifecycleStatus != ProjectLifecycleStatus.Completed)\n        {\n            return ProjectTotUpdateResult.ValidationFailed(\n                "Transfer of Technology can be updated only after the project is completed and while the project is operationally editable.");\n        }\n'''
    text = replace_once(text, '''        if (project is null)\n        {\n            return ProjectTotUpdateResult.NotFound();\n        }\n''', guard_update, "service update guard")

    old_submit = '''        if (project is null)\n        {\n            return ProjectTotRequestActionResult.NotFound();\n        }\n\n        // ToT is a project-level portfolio record throughout the lifecycle. It may be\n        // recorded as Not required, Not started, In progress or Completed at any stage.\n'''
    new_submit = '''        if (project is null)\n        {\n            return ProjectTotRequestActionResult.NotFound();\n        }\n\n        if (project.IsDeleted || project.IsArchived || project.LifecycleStatus != ProjectLifecycleStatus.Completed)\n        {\n            return ProjectTotRequestActionResult.ValidationFailed(\n                "Transfer of Technology can be submitted only after the project is completed and while the project is operationally editable.");\n        }\n'''
    text = replace_once(text, old_submit, new_submit, "service submit guard")

    text = replace_once(text, '''        totRequest.ProposedStatus = normalizedRequest.Status;\n        totRequest.ProposedStartedOn = normalizedRequest.StartedOn;\n        totRequest.ProposedCompletedOn = normalizedRequest.CompletedOn;\n''', '''        totRequest.ProposedStatus = normalizedRequest.Status;\n        totRequest.ProposedStartedOn = normalizedRequest.StartedOn;\n        totRequest.ProposedStartDatePrecision = normalizedRequest.StartDatePrecision;\n        totRequest.ProposedCompletedOn = normalizedRequest.CompletedOn;\n        totRequest.ProposedCompletionDatePrecision = normalizedRequest.CompletionDatePrecision;\n''', "service request precision")

    text = replace_once(text, '''        if (approve)\n        {\n            var updateRequest = new ProjectTotUpdateRequest(\n                request.ProposedStatus,\n                request.ProposedStartedOn,\n                PartialDatePrecision.Day,\n                request.ProposedCompletedOn,\n                PartialDatePrecision.Day,\n''', '''        if (approve)\n        {\n            if (project.IsDeleted || project.IsArchived || project.LifecycleStatus != ProjectLifecycleStatus.Completed)\n            {\n                return ProjectTotRequestActionResult.ValidationFailed(\n                    "The project must remain completed and operationally editable before a Transfer of Technology request can be approved.");\n            }\n\n            var updateRequest = new ProjectTotUpdateRequest(\n                request.ProposedStatus,\n                request.ProposedStartedOn,\n                request.ProposedStartDatePrecision,\n                request.ProposedCompletedOn,\n                request.ProposedCompletionDatePrecision,\n''', "service approval precision")

    text = replace_once(text, '''            case ProjectTotStatus.NotRequired:\n            case ProjectTotStatus.NotStarted:\n                tot.StartedOn = null;\n                tot.CompletedOn = null;\n''', '''            case ProjectTotStatus.NotRequired:\n            case ProjectTotStatus.NotStarted:\n                tot.StartedOn = null;\n                tot.StartDatePrecision = PartialDatePrecision.None;\n                tot.CompletedOn = null;\n                tot.CompletionDatePrecision = PartialDatePrecision.None;\n''', "service clear precision")
    text = replace_once(text, '''            case ProjectTotStatus.InProgress:\n                tot.StartedOn = request.StartedOn;\n                tot.CompletedOn = null;\n                break;\n            case ProjectTotStatus.Completed:\n                tot.StartedOn = request.StartedOn;\n                tot.CompletedOn = request.CompletedOn;\n                break;\n''', '''            case ProjectTotStatus.InProgress:\n                tot.StartedOn = request.StartedOn;\n                tot.StartDatePrecision = request.StartDatePrecision;\n                tot.CompletedOn = null;\n                tot.CompletionDatePrecision = PartialDatePrecision.None;\n                break;\n            case ProjectTotStatus.Completed:\n                tot.StartedOn = request.StartedOn;\n                tot.StartDatePrecision = request.StartedOn.HasValue\n                    ? request.StartDatePrecision\n                    : PartialDatePrecision.None;\n                tot.CompletedOn = request.CompletedOn;\n                tot.CompletionDatePrecision = request.CompletionDatePrecision;\n                break;\n''', "service apply precision")
    write("Services/Projects/ProjectTotService.cs", text)


def build_edit_page() -> None:
    text = read("Pages/Projects/Tot/Edit.cshtml.cs")
    text = replace_once(text, '''        if (!CanManageTot)\n        {\n            return DenyProjectAccess(project.Id);\n        }\n\n        PopulateInputFromProject(project);\n''', '''        if (!CanManageTot)\n        {\n            return DenyProjectAccess(project.Id);\n        }\n\n        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed || project.IsArchived || project.IsDeleted)\n        {\n            TempData["Error"] = "Transfer of Technology can be updated only after the project is completed and while it is operationally editable.";\n            return RedirectToPage("/Projects/Overview", new { id = project.Id });\n        }\n\n        PopulateInputFromProject(project);\n''', "edit get guard")
    text = replace_once(text, '''        Input.StartDatePrecision = Input.StartedOn.HasValue\n            ? PartialDatePrecision.Day\n            : PartialDatePrecision.None;\n        Input.CompletedOn = project.Tot?.CompletedOn;\n        Input.CompletionDatePrecision = Input.CompletedOn.HasValue\n            ? PartialDatePrecision.Day\n            : PartialDatePrecision.None;\n''', '''        Input.StartDatePrecision = project.Tot?.StartDatePrecision ?? PartialDatePrecision.None;\n        Input.CompletedOn = project.Tot?.CompletedOn;\n        Input.CompletionDatePrecision = project.Tot?.CompletionDatePrecision ?? PartialDatePrecision.None;\n''', "edit load precision")
    text = replace_once(text, '''        if (Input.StartedOn is { } start)\n        {\n            Input.StartYear = start.Year;\n            Input.StartMonth = start.Month;\n            Input.StartDay = start.Day;\n        }\n\n        if (Input.CompletedOn is { } completion)\n        {\n            Input.CompletionYear = completion.Year;\n            Input.CompletionMonth = completion.Month;\n            Input.CompletionDay = completion.Day;\n        }\n''', '''        if (Input.StartedOn is { } start)\n        {\n            Input.StartYear = start.Year;\n            Input.StartMonth = Input.StartDatePrecision >= PartialDatePrecision.Month ? start.Month : null;\n            Input.StartDay = Input.StartDatePrecision == PartialDatePrecision.Day ? start.Day : null;\n        }\n\n        if (Input.CompletedOn is { } completion)\n        {\n            Input.CompletionYear = completion.Year;\n            Input.CompletionMonth = Input.CompletionDatePrecision >= PartialDatePrecision.Month ? completion.Month : null;\n            Input.CompletionDay = Input.CompletionDatePrecision == PartialDatePrecision.Day ? completion.Day : null;\n        }\n''', "edit partial fields")
    write("Pages/Projects/Tot/Edit.cshtml.cs", text)


def build_header() -> None:
    text = read("Pages/Projects/_ProjectCommandHeader.cshtml", v2=True)
    text = text.replace("@using System.Linq\n", "")
    start = '''        else if (isCompleted)\n        {\n            <button id="project-tot-card"'''
    a = text.find(start)
    b = text.find('''        else\n        {\n            <div class="project-intelligence-card" role="group"''', a)
    if a < 0 or b < 0:
        raise RuntimeError("command header ToT block not found")
    replacement = '''        else if (isCompleted)\n        {\n            @await Component.InvokeAsync("ProjectTotCommandCard", new\n            {\n                projectId = project!.Id,\n                canManage = Model.CanManageTot\n            })\n        }\n'''
    text = text[:a] + replacement + text[b:]
    # Remove obsolete ToT calculation block from the top.
    calc_start = text.find("    var tot = Model.TotSummary;")
    calc_end = text.find("\n\n    static string DisplayUser", calc_start)
    if calc_start >= 0 and calc_end >= 0:
        text = text[:calc_start] + text[calc_end + 2:]
    write("Pages/Projects/_ProjectCommandHeader.cshtml", text)


def build_overview_tot() -> None:
    text = read("Pages/Projects/Overview.Tot.cs", v2=True)
    text = text.replace("using System.ComponentModel.DataAnnotations;\n", "using System.ComponentModel.DataAnnotations;\nusing System.Globalization;\n")
    text = text.replace("using ProjectManagement.Models;\n", "using ProjectManagement.Models;\nusing ProjectManagement.Models.Remarks;\n")
    text = replace_once(text, '''                        item.Tot.Status,\n                        item.Tot.StartedOn,\n                        item.Tot.CompletedOn,\n''', '''                        item.Tot.Status,\n                        item.Tot.StartedOn,\n                        item.Tot.StartDatePrecision,\n                        item.Tot.CompletedOn,\n                        item.Tot.CompletionDatePrecision,\n''', "overview tot query precision")
    text = replace_once(text, '''                startYear = tot?.StartedOn?.Year,\n                startMonth = tot?.StartedOn?.Month,\n                startDay = tot?.StartedOn?.Day,\n                completionYear = tot?.CompletedOn?.Year,\n                completionMonth = tot?.CompletedOn?.Month,\n                completionDay = tot?.CompletedOn?.Day,\n''', '''                startYear = tot?.StartedOn?.Year,\n                startMonth = tot is not null && tot.StartDatePrecision >= PartialDatePrecision.Month ? tot.StartedOn?.Month : null,\n                startDay = tot is not null && tot.StartDatePrecision == PartialDatePrecision.Day ? tot.StartedOn?.Day : null,\n                completionYear = tot?.CompletedOn?.Year,\n                completionMonth = tot is not null && tot.CompletionDatePrecision >= PartialDatePrecision.Month ? tot.CompletedOn?.Month : null,\n                completionDay = tot is not null && tot.CompletionDatePrecision == PartialDatePrecision.Day ? tot.CompletedOn?.Day : null,\n''', "overview tot input precision")
    text = replace_once(text, '''            card = BuildTotCard(status, hasRecord, pending?.ProposedStatus),\n            summary = BuildTotSummaryPayload(tot)\n''', '''            card = BuildTotCard(status, hasRecord, pending?.ProposedStatus,\n                tot?.StartedOn, tot?.StartDatePrecision ?? PartialDatePrecision.None,\n                tot?.CompletedOn, tot?.CompletionDatePrecision ?? PartialDatePrecision.None),\n            summary = BuildTotSummaryPayload(tot),\n            latestRemark = await LoadLatestTotRemarkPayloadAsync(id, ct)\n''', "overview tot get payload")
    text = replace_once(text, '''            card = BuildTotCard(input.Status, true, null)\n''', '''            card = BuildTotCard(input.Status, true, null,\n                request!.StartedOn, request.StartDatePrecision,\n                request.CompletedOn, request.CompletionDatePrecision)\n''', "overview tot save card")

    handler_marker = "    private async Task<bool> CanManageTotFromOverviewAsync("
    handler = '''    public async Task<IActionResult> OnPostTotRemarkAsync(\n        int id,\n        [FromForm] int projectId,\n        [FromForm] string? body,\n        CancellationToken ct)\n    {\n        if (id <= 0 || projectId != id)\n        {\n            return new JsonResult(new { error = "The ToT remark form is not valid for this project." })\n            {\n                StatusCode = StatusCodes.Status400BadRequest\n            };\n        }\n\n        var project = await _db.Projects\n            .AsNoTracking()\n            .Where(item => item.Id == id && !item.IsDeleted)\n            .Select(item => new { item.LifecycleStatus, item.IsArchived, item.LeadPoUserId })\n            .SingleOrDefaultAsync(ct);\n\n        if (project is null)\n        {\n            return new JsonResult(new { error = "Project not found." })\n            {\n                StatusCode = StatusCodes.Status404NotFound\n            };\n        }\n\n        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed || project.IsArchived ||\n            !await CanManageTotFromOverviewAsync(id, project.LeadPoUserId, project.IsArchived, ct))\n        {\n            return new JsonResult(new { error = "You are not authorised to add a ToT remark for this project." })\n            {\n                StatusCode = StatusCodes.Status403Forbidden\n            };\n        }\n\n        var normalized = body?.Trim();\n        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 4 || normalized.Length > 2000)\n        {\n            return new JsonResult(new { error = "Remarks must contain between 4 and 2000 characters." })\n            {\n                StatusCode = StatusCodes.Status400BadRequest\n            };\n        }\n\n        var userId = _users.GetUserId(User);\n        if (string.IsNullOrWhiteSpace(userId)) return Forbid();\n\n        var role = User.IsInRole("Admin")\n            ? RemarkActorRole.Administrator\n            : User.IsInRole("HoD")\n                ? RemarkActorRole.HeadOfDepartment\n                : RemarkActorRole.ProjectOfficer;\n\n        var remark = new Remark\n        {\n            ProjectId = id,\n            AuthorUserId = userId,\n            AuthorRole = role,\n            Type = RemarkType.Internal,\n            Scope = RemarkScope.TransferOfTechnology,\n            Body = normalized,\n            EventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst())),\n            CreatedAtUtc = _clock.UtcNow.UtcDateTime,\n            RowVersion = Guid.NewGuid().ToByteArray()\n        };\n        _db.Remarks.Add(remark);\n        await _db.SaveChangesAsync(ct);\n\n        return new JsonResult(new { success = true, message = "Transfer of Technology remark added." });\n    }\n\n    private async Task<object?> LoadLatestTotRemarkPayloadAsync(int projectId, CancellationToken ct)\n    {\n        var latest = await _db.Remarks\n            .AsNoTracking()\n            .Where(item => item.ProjectId == projectId && !item.IsDeleted && item.Scope == RemarkScope.TransferOfTechnology)\n            .OrderByDescending(item => item.CreatedAtUtc)\n            .ThenByDescending(item => item.Id)\n            .Select(item => new { item.Body, item.Type, item.AuthorUserId, item.CreatedAtUtc })\n            .FirstOrDefaultAsync(ct);\n        if (latest is null) return null;\n        var author = await _db.Users.AsNoTracking()\n            .Where(item => item.Id == latest.AuthorUserId)\n            .Select(item => item.FullName ?? item.UserName ?? item.Email ?? item.Id)\n            .FirstOrDefaultAsync(ct) ?? "Unknown";\n        return new\n        {\n            body = latest.Body,\n            meta = $"{(latest.Type == RemarkType.External ? "External" : "Internal")} · {author}"\n        };\n    }\n\n'''
    text = text.replace(handler_marker, handler + handler_marker, 1)

    replacement_methods = '''    private static object BuildTotCard(\n        ProjectTotStatus status,\n        bool hasRecord,\n        ProjectTotStatus? proposedStatus,\n        DateOnly? startedOn,\n        PartialDatePrecision startPrecision,\n        DateOnly? completedOn,\n        PartialDatePrecision completionPrecision)\n    {\n        if (proposedStatus.HasValue)\n        {\n            return new { title = "Approval pending", summary = $"Proposed: {TotStatusLabel(proposedStatus.Value)}", tone = "info" };\n        }\n\n        var title = hasRecord ? TotStatusLabel(status) : "Not recorded";\n        var summary = status switch\n        {\n            ProjectTotStatus.NotRequired => "No ToT action required",\n            ProjectTotStatus.NotStarted => hasRecord ? "ToT action pending" : "Record ToT position",\n            ProjectTotStatus.InProgress when startedOn.HasValue => $"Started {FormatPartialDate(startedOn.Value, startPrecision)}",\n            ProjectTotStatus.InProgress => "ToT is in progress",\n            ProjectTotStatus.Completed when completedOn.HasValue => $"Completed {FormatPartialDate(completedOn.Value, completionPrecision)}",\n            ProjectTotStatus.Completed => "ToT marked completed",\n            _ => "Record ToT position"\n        };\n        var tone = status switch\n        {\n            ProjectTotStatus.Completed => "positive",\n            ProjectTotStatus.InProgress => "info",\n            ProjectTotStatus.NotRequired => "neutral",\n            _ => "warning"\n        };\n        return new { title, summary, tone };\n    }\n\n    private static object BuildTotSummaryPayload(ProjectTot? tot)\n    {\n        if (tot is null)\n        {\n            return new\n            {\n                hasRecord = false,\n                statusLabel = "Not recorded",\n                summary = "No Transfer of Technology position has been recorded.",\n                facts = Array.Empty<object>()\n            };\n        }\n\n        var facts = new System.Collections.Generic.List<object>();\n        if (tot.StartedOn.HasValue) facts.Add(new { label = "Started on", value = FormatPartialDate(tot.StartedOn.Value, tot.StartDatePrecision) });\n        if (tot.CompletedOn.HasValue) facts.Add(new { label = "Completed on", value = FormatPartialDate(tot.CompletedOn.Value, tot.CompletionDatePrecision) });\n        if (!string.IsNullOrWhiteSpace(tot.MetDetails)) facts.Add(new { label = "MET details", value = tot.MetDetails });\n        if (tot.MetCompletedOn.HasValue) facts.Add(new { label = "MET completed on", value = tot.MetCompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) });\n        if (tot.FirstProductionModelManufactured.HasValue) facts.Add(new { label = "First production model", value = tot.FirstProductionModelManufactured.Value ? "Manufactured" : "Not manufactured" });\n        if (tot.FirstProductionModelManufacturedOn.HasValue) facts.Add(new { label = "Manufactured on", value = tot.FirstProductionModelManufacturedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) });\n\n        var summary = tot.Status switch\n        {\n            ProjectTotStatus.NotRequired => "Transfer of Technology is not required for this project.",\n            ProjectTotStatus.NotStarted => "Transfer of Technology has not started.",\n            ProjectTotStatus.InProgress => "Transfer of Technology is in progress.",\n            ProjectTotStatus.Completed => "Transfer of Technology is completed.",\n            _ => "Transfer of Technology details are unavailable."\n        };\n        return new { hasRecord = true, statusLabel = TotStatusLabel(tot.Status), summary, facts };\n    }\n\n    private static string FormatPartialDate(DateOnly value, PartialDatePrecision precision) => precision switch\n    {\n        PartialDatePrecision.Year => value.Year.ToString(CultureInfo.InvariantCulture),\n        PartialDatePrecision.Month => value.ToString("MMM yyyy", CultureInfo.InvariantCulture),\n        _ => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)\n    };\n\n'''
    text = replace_between(text, "    private static object BuildTotCard(", "    private static string TotStatusLabel", replacement_methods, "overview tot methods")
    write("Pages/Projects/Overview.Tot.cs", text)


def build_overview_page() -> None:
    text = read("Pages/Projects/Overview.cshtml", v2=True)
    text = replace_once(text, '''    <script src="~/js/projects/overview-tot.js" asp-append-version="true"></script>\n''', '''    <script src="~/js/projects/overview-tot.js" asp-append-version="true"></script>\n    <script src="~/js/projects/cover-photo-fallback.js" asp-append-version="true"></script>\n''', "overview cover fallback script")
    write("Pages/Projects/Overview.cshtml", text)
    write("Pages/Projects/_ProjectWorkspaceHosts.cshtml", read("Pages/Projects/_ProjectWorkspaceHosts.cshtml", v2=True))
    write("wwwroot/css/pages/project-tot-drawer.css", read("wwwroot/css/pages/project-tot-drawer.css", v2=True) + '''\n.project-tot-footer-group{display:flex;justify-content:flex-end;gap:.5rem;width:100%;flex-wrap:wrap}.project-tot-remark-editor{border-top:1px solid var(--bs-border-color);padding-top:1rem}.project-tot-additional>summary{cursor:pointer;font-weight:600}.project-tot-drawer [data-tot-success],.project-tot-drawer [data-tot-global-error]{position:sticky;top:0;z-index:2}\n''')


def build_cover_fallback() -> None:
    write("wwwroot/js/projects/cover-photo-fallback.js", '''(() => {\n    'use strict';\n    function reveal(image) {\n        if (!(image instanceof HTMLImageElement)) return;\n        const picture = image.closest('picture');\n        const host = image.closest('[data-project-cover-host]') || picture?.parentElement || image.parentElement;\n        const fallback = host?.querySelector('[data-project-cover-fallback]') || document.querySelector('[data-project-cover-fallback]');\n        picture?.remove();\n        if (!picture) image.remove();\n        fallback?.classList.remove('d-none');\n    }\n    function wire(image) {\n        if (!(image instanceof HTMLImageElement) || image.dataset.coverFallbackWired === '1') return;\n        image.dataset.coverFallbackWired = '1';\n        image.addEventListener('error', () => reveal(image), { once: true });\n        if (image.complete && image.naturalWidth === 0) reveal(image);\n    }\n    document.addEventListener('error', (event) => {\n        const image = event.target;\n        if (image instanceof HTMLImageElement && image.matches('[data-project-cover-image]')) reveal(image);\n    }, true);\n    document.querySelectorAll('[data-project-cover-image]').forEach(wire);\n})();\n''')


def build_migration() -> None:
    migration = '''using Microsoft.EntityFrameworkCore.Infrastructure;\nusing Microsoft.EntityFrameworkCore.Migrations;\nusing ProjectManagement.Data;\n\n#nullable disable\n\nnamespace ProjectManagement.Migrations;\n\n[DbContext(typeof(ApplicationDbContext))]\n[Migration("20261206100000_AddProjectTotDatePrecision")]\npublic partial class AddProjectTotDatePrecision : Migration\n{\n    protected override void Up(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.AddColumn<int>(name: "StartDatePrecision", table: "ProjectTots", type: "integer", nullable: false, defaultValue: 0);\n        migrationBuilder.AddColumn<int>(name: "CompletionDatePrecision", table: "ProjectTots", type: "integer", nullable: false, defaultValue: 0);\n        migrationBuilder.AddColumn<int>(name: "ProposedStartDatePrecision", table: "ProjectTotRequests", type: "integer", nullable: false, defaultValue: 0);\n        migrationBuilder.AddColumn<int>(name: "ProposedCompletionDatePrecision", table: "ProjectTotRequests", type: "integer", nullable: false, defaultValue: 0);\n\n        migrationBuilder.Sql("UPDATE \\"ProjectTots\\" SET \\"StartDatePrecision\\" = 3 WHERE \\"StartedOn\\" IS NOT NULL;");\n        migrationBuilder.Sql("UPDATE \\"ProjectTots\\" SET \\"CompletionDatePrecision\\" = 3 WHERE \\"CompletedOn\\" IS NOT NULL;");\n        migrationBuilder.Sql("UPDATE \\"ProjectTotRequests\\" SET \\"ProposedStartDatePrecision\\" = 3 WHERE \\"ProposedStartedOn\\" IS NOT NULL;");\n        migrationBuilder.Sql("UPDATE \\"ProjectTotRequests\\" SET \\"ProposedCompletionDatePrecision\\" = 3 WHERE \\"ProposedCompletedOn\\" IS NOT NULL;");\n\n        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTots_StartDatePrecision", table: "ProjectTots", sql: "\\\"StartDatePrecision\\\" BETWEEN 0 AND 3");\n        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTots_CompletionDatePrecision", table: "ProjectTots", sql: "\\\"CompletionDatePrecision\\\" BETWEEN 0 AND 3");\n        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTotRequests_StartDatePrecision", table: "ProjectTotRequests", sql: "\\\"ProposedStartDatePrecision\\\" BETWEEN 0 AND 3");\n        migrationBuilder.AddCheckConstraint(name: "CK_ProjectTotRequests_CompletionDatePrecision", table: "ProjectTotRequests", sql: "\\\"ProposedCompletionDatePrecision\\\" BETWEEN 0 AND 3");\n    }\n\n    protected override void Down(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTots_StartDatePrecision", table: "ProjectTots");\n        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTots_CompletionDatePrecision", table: "ProjectTots");\n        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTotRequests_StartDatePrecision", table: "ProjectTotRequests");\n        migrationBuilder.DropCheckConstraint(name: "CK_ProjectTotRequests_CompletionDatePrecision", table: "ProjectTotRequests");\n        migrationBuilder.DropColumn(name: "StartDatePrecision", table: "ProjectTots");\n        migrationBuilder.DropColumn(name: "CompletionDatePrecision", table: "ProjectTots");\n        migrationBuilder.DropColumn(name: "ProposedStartDatePrecision", table: "ProjectTotRequests");\n        migrationBuilder.DropColumn(name: "ProposedCompletionDatePrecision", table: "ProjectTotRequests");\n    }\n}\n'''
    write("Migrations/20261206100000_AddProjectTotDatePrecision.cs", migration)
    ids = read("Migrations/immutable-migration-ids.txt")
    if "20261206100000_AddProjectTotDatePrecision" not in ids:
        ids = ids.rstrip() + "\n20261206100000_AddProjectTotDatePrecision\n"
    write("Migrations/immutable-migration-ids.txt", ids)


def build_templates() -> None:
    copy_template("Pages/Projects/_ProjectTotDrawer.cshtml")
    copy_template("wwwroot/js/projects/overview-tot.js")
    copy_template("ViewComponents/ProjectTotCommandCardViewComponent.cs")
    copy_template("Pages/Shared/Components/ProjectTotCommandCard/Default.cshtml")


def build_readme() -> None:
    readme = (PACKAGE_ROOT / "README-APPLY-FIRST.md").read_text(encoding="utf-8")
    write("README-APPLY-FIRST.md", readme)


def main() -> None:
    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)
    build_models()
    build_service()
    build_edit_page()
    build_header()
    build_overview_tot()
    build_overview_page()
    build_cover_fallback()
    build_migration()
    build_templates()
    build_readme()
    print(f"Generated replacement package at {OUT}")


if __name__ == "__main__":
    main()
