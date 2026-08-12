# Brochure Builder — print pagination and Cover A fix

This bundle contains complete replacement files, preserving their solution-relative paths.

## Behaviour implemented

- Keeps each print project module atomic in QuestPDF; a measurement mismatch can no longer render
  an untitled continuation on a following page.
- Verifies that float-layout narrative partitioning reproduces the source narrative exactly.
- Reserves one body-line safety allowance for SkiaSharp/QuestPDF shaping differences.
- Packs non-final sheets from the front and deliberately carries unavoidable residual space to the
  final physical sheet.
- Excludes the final sheet from underfill scoring, Smart Flow fill diagnostics, warning styling,
  and residual-padding polish.
- Allows a measured five-project sheet at the existing 9 pt typography floor, using bounded search
  pruning; four projects remains the preferred reference density.
- Treats all shipped Cover A artwork as identity-complete and does not overlay duplicate
  organisation logos. Logo assets remain available only for missing-artwork fallback composition
  and for the separate Contemporary cover style.
- Updates preflight labels and sheet chips so the final sheet is clearly identified as
  `final / residual allowed`.

## Apply

Copy the files over the matching paths in the ProjectManagement solution. No database migration or
package change is required.

## Validate

```powershell
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~Publications"
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

Then regenerate the same nine-project Print / Compact benchmark and confirm:

1. Every project appears once, with its title and border intact.
2. Non-final sheets are forward-packed.
3. Any unavoidable blank area appears on the final sheet.
4. Cover A shows only the identity marks embedded in the selected artwork.
