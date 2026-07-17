# PRISM ERP — IPR Module Write-Workflow Stabilisation

## Installation

1. Back up the current solution or commit the existing working tree.
2. Copy the contents of this folder into the **ProjectManagement solution root**.
3. Allow all existing files to be overwritten.
4. Retain all five new `Index.*.cs` partial-class files and both new Razor partials.
5. Clean and rebuild the solution.

The project uses SDK-style compile globs, so the new `.cs` files are included automatically. **No database migration or configuration change is required.**

## What this implementation corrects

- Explicit Razor Page and named-handler routing for create, edit, delete, attachment upload and attachment removal.
- Filter, tab and pagination preservation through dedicated hidden state fields rather than POST route dictionaries.
- Isolation of validation state for the command being submitted, preventing unrelated bound forms from blocking save.
- Reliable project assignment, reassignment and removal on existing IPR records.
- Server-side rejection of nonexistent or soft-deleted project IDs, while historical archived projects remain selectable.
- Searchable, keyboard-accessible project combobox with project name, case-file number and lifecycle information.
- Preservation of submitted values and a fully populated register when validation or concurrency checks fail.
- Busy states and duplicate-submission prevention for save and attachment upload operations.
- Edit controls hidden for users without IPR edit permission.
- The oversized PageModel is split by responsibility into record commands, attachment commands, loading, selection/state logic and nested models.

## Verification commands

Run from the solution root:

```powershell
dotnet build .\ProjectManagement.csproj -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --filter "FullyQualifiedName~Ipr"
node --test .\wwwroot\js\project-office-reports\ipr\index.test.js
```

## Manual acceptance checks

1. Open an unassigned IPR record, select a project and save.
2. Confirm the same project remains linked after page refresh.
3. Reassign the record to another project and save.
4. Select **No project**, save and confirm that the record becomes unassigned.
5. Confirm that saving never produces the temporary zero-KPI/zero-record page.
6. Submit a validation error and confirm the drawer remains open, entered values remain intact, and the register/KPIs remain populated.
7. Upload and remove a PDF attachment, then delete a test record.
8. Verify project search using partial project name, case-file number and lifecycle text, including keyboard navigation.

## Files included

### Application
- `Application/Ipr/IprValidationCode.cs`
- `Application/Ipr/IprWriteService.cs`

### IPR Razor Page
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.RecordCommands.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.AttachmentCommands.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.Loading.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.SelectLists.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.Models.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/_RecordFormPartial.cshtml`
- `Areas/ProjectOfficeReports/Pages/Ipr/_RegisterStateFields.cshtml`
- `Areas/ProjectOfficeReports/Pages/Ipr/_ProjectSearchPicker.cshtml`

### Frontend
- `wwwroot/js/project-office-reports/ipr/index.js`
- `wwwroot/js/project-office-reports/ipr/index.test.js`
- `wwwroot/css/site-ipr.css`

### Regression tests
- `ProjectManagement.Tests/IprIndexPageTests.cs`
- `ProjectManagement.Tests/IprWriteServiceTests.cs`
