# IPR definitive build fix

Replace the following files from the project root:

1. `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml`
2. `Areas/ProjectOfficeReports/Pages/Ipr/_ProjectSearchPicker.cshtml`
3. `Services/Admin/MasterData/CelebrationAdministrationService.cs`

## Corrections

- `FormatAge` is now a Razor-local display helper. The view no longer depends on a static member being discovered on a partial `IndexModel` during Razor design-time compilation.
- The project picker no longer dereferences nullable `ProjectId.Value`.
- Celebration update logic no longer dereferences or pattern-flows a nullable GUID; it uses a non-nullable local after the create/update branch is established.
- `CS0006` in `ProjectManagement.Tests` is a downstream error and disappears once the main `ProjectManagement` project compiles.

## Rebuild

Stop the running application, replace the files, then delete the `bin` and `obj` directories for both projects and rebuild the solution.
