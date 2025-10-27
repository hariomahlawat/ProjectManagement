# Training tracker quick reference

This cheat sheet highlights the everyday actions available to Teaching Assistants (TA), Project Officers (PO), Administrators, and Heads of Department (HoD) inside the Project Office → Training workspace.

## TA & Project Officer essentials

These roles can view the tracker, slice the data, and download exports to support planning.

- **Open the tracker:** Go to **Project Office Reports → Training**. Viewer access is granted to Admin, HoD, Project Office, Project Officer, TA, Comdt, MCO, and Main Office roles. 【F:Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs†L35-L88】【F:Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs†L200-L226】
- **Apply filters:** Use the Filters button to target specific training types, linked project technical categories, participant categories (Officers/JCO/OR), date ranges, and free-text search. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml†L10-L115】
- **Review KPIs:** The dashboard cards show total trainings, total trainees, and per-type breakdowns to gauge readiness at a glance. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml†L38-L80】
- **Download Excel extracts:** Select **Export to Excel**, adjust the date range and optional technical category, and toggle roster inclusion when detailed attendance is needed. Exports respect the active filters. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml†L20-L170】【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs†L101-L179】
- **Understand roster visibility:** If rosters exist for a training, exported files include individual attendee records; otherwise, only aggregate counts are shown. Legacy records without rosters are labelled accordingly. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L108-L190】

## Admin & HoD operations

Administrators and HoDs can create/update trainings, manage rosters, and process delete requests.

- **Create or edit trainings:** Use **New training** or open an existing row to launch the editor. Admin/HoD plus Project Office roles have manage rights. Fill schedule details, select training type, add linked projects, and (optionally) paste roster payloads to auto-calculate participant counters. 【F:Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs†L74-L113】【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml†L24-L52】【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L24-L204】
- **Handle legacy records:** Toggle the legacy mode when no roster is available; the form switches to manual officer/JCO/OR counts while keeping roster uploads disabled. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L120-L170】
- **Manage rosters:** Upload or paste roster JSON/CSV (handled client-side) to populate trainee rows. The system deduplicates by Army number per training and maps ranks to Officer/JCO/OR categories automatically. 【F:Migrations/20251221120000_AddTrainingRoster.cs†L14-L45】【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L108-L190】
- **Link projects:** Associate trainings with Projects via the project picker so stakeholders can trace readiness investments. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L169-L207】【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L52-L140】
- **Submit delete requests:** From the editor, request deletion with a reason; the system records the request and notifies approvers. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L205-L331】【F:Services/ProjectOfficeReports/Training/TrainingNotificationService.cs†L14-L87】
- **Approve/decline deletions:** Visit **Training approvals** to review pending requests, view context, and take action. Only Admin/HoD roles satisfy the approval policy. 【F:Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs†L200-L226】【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml†L27-L52】【F:Areas/ProjectOfficeReports/Pages/Training/Approvals.cshtml†L1-L80】
- **Feature flag awareness:** If the tracker is disabled, manage/export actions are blocked until `ProjectOfficeReports:TrainingTracker:Enabled` is true. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs†L78-L110】【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L60-L108】

Keep this reference handy during rollout so each persona understands their responsibilities without combing through the full user manual.
