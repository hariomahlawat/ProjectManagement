# PRISM Admin Phase B build hotfix

Apply after `PRISM-Admin-PhaseB-Core-Architecture-and-Consistency-Ready-to-Replace.zip`.

## Files

- `Areas/Admin/Pages/Diagnostics/DbHealth.cshtml`
- `Areas/Admin/Pages/Users/Index.cshtml`
- `Areas/Admin/Pages/Index.cshtml.cs`

## Corrections

1. Qualifies the static `DbHealthModel.FormatBytes` and `DbHealthModel.StatusCss` helpers by type.
2. Renames the Razor pagination loop variable from the reserved directive name `page` to `pageNumber`.
3. Imports `Microsoft.AspNetCore.Mvc` so the `IUrlHelper.Page(...)` extension is available.

No database migration is required.
