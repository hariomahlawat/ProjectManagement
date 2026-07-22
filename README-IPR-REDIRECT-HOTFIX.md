# IPR post-save redirect hotfix

## Fault corrected

The IPR mutation handlers were returning:

```csharp
RedirectToPage(null, routeValues)
```

In the running Razor Pages application this produces a `RedirectToPageResult` with an empty page name. The record transaction completes, but execution then fails with:

```text
InvalidOperationException: No page named '' matches the supplied values.
```

Every affected handler now explicitly redirects to the current IPR index page:

```csharp
RedirectToPage("./Index", routeValues)
```

## Replace these files

1. `Areas/ProjectOfficeReports/Pages/Ipr/Index.RecordCommands.cs`
2. `Areas/ProjectOfficeReports/Pages/Ipr/Index.AttachmentCommands.cs`

The included test file is recommended but is not required to run the application:

3. `ProjectManagement.Tests/IprIndexPageTests.cs`

## Handlers corrected

- Create record
- Edit record
- Delete record
- Upload attachment
- Remove attachment

## Verification

1. Rebuild and run the application.
2. Open an unassigned IPR record.
3. Select a project and choose **Save changes**.
4. Confirm that the browser returns to `/ProjectOfficeReports/Ipr?...` without `handler=Edit`.
5. Reopen the record and confirm the selected project remains assigned.
6. Test create, delete, upload attachment and remove attachment because the same redirect fault existed in those handlers.

No database migration is required.
