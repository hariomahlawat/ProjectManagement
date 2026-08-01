# PRISM ERP — IPR Register Access and Navigation Stabilisation

## Apply

1. Stop the application or IIS application pool.
2. Back up or commit the current solution.
3. Copy the contents of this package over the **ProjectManagement solution root**.
4. Replace the files listed in `REPLACEMENT-MANIFEST.txt`.
5. Delete stale `bin` and `obj` directories if Visual Studio retains old Razor output.
6. Rebuild and run the focused validation commands below.
7. Restart the application and perform the acceptance checks.

No database migration or configuration update is required.

## Behaviour delivered

- Every authenticated PRISM user can view the IPR Register, its projects, follow-up, analytics, exports and public supporting evidence.
- Existing IPR edit roles remain unchanged; read access does not grant create, edit, delete or evidence-upload authority.
- Numeric pagination uses `pageNumber`, avoiding the Razor Pages `page` routing collision.
- A compact pager is visible in the Records toolbar and a full pager remains below the table.
- Search, category, status, project, year and page-size state are preserved across pagination.
- Page-size and filter changes return to page 1 and clear stale record/edit state.
- Invalid or out-of-range pages are clamped to a valid page.
- `selectedRecordId` is independent from edit mode.
- A requested record is located on its actual filtered/sorted page and selected in the inspector.
- Global search opens a neutral IPR record view; only authorised editors receive an Edit action.
- Public evidence downloads are limited to records visible in the public IPR Register.
- Natural browser-page scrolling is retained; no nested vertical scrollbar is reintroduced.
- Navigation terminology is standardised as **IPR Register**.

## Focused validation

```powershell
dotnet build .\ProjectManagement.csproj -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --filter "FullyQualifiedName~Ipr|FullyQualifiedName~RoleBasedNavigationProvider"
node --check .\wwwroot\js\project-office-reports\ipr\index.js
node --test .\wwwroot\js\project-office-reports\ipr\index.test.js
```

## Manual acceptance checks

1. Sign in as a Project Officer, TA, ITO or clerk and confirm **IPR Register** is visible and opens successfully.
2. Confirm read-only users do not see or reach create, edit, delete or evidence-upload operations.
3. Set Rows to 10 and use the top and bottom controls to navigate pages 1, 2 and 3/4 as applicable.
4. Confirm the URL uses `pageNumber`, for example `?tab=records&pageSize=10&pageNumber=2`.
5. Apply each filter and verify pagination preserves the filter while a filter change returns to page 1.
6. Open an IPR result from global search and confirm the correct record and page open without the edit drawer.
7. Sign in as an IPR editor and confirm Edit opens the selected record and all existing write workflows remain operational.
8. Enter an excessively high `pageNumber` and confirm the final valid page is loaded.
9. Confirm the page uses only the browser's normal vertical scrollbar.
