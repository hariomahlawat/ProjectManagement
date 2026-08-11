# PRISM Publications — Phase 10 Reference-Fidelity Print Composition

## Purpose

Phase 10 is an incremental refinement of the Phase 9 measured Print / Compact implementation.
It targets the remaining visual differences identified by direct comparison with the approved
11-page reference brochure while leaving **Digital / Comfortable** unchanged.

The important architectural change is the hard-copy project module itself:

- photographs are always anchored at the **upper-right**;
- narrative copy is measured to flow beside the image stack;
- remaining narrative returns to the **full card width below the imagery**;
- the old alternating left/right hard-copy image composition is removed.

This reproduces the information-density mechanism used by the approved reference brochure
instead of trying to obtain density by reducing typography.

## What changed

### 1. Reference-style text/image flow

`BrochurePrintMeasurementService` now splits each photographed Project Brief at a measured
word boundary. The leading segment fits beside the image or Gallery 2 stack; the remainder is
measured at full module width. The same measured split is consumed by QuestPDF, so preflight
and final rendering remain aligned.

### 2. Print-readable project typography

Print / Compact now uses bounded variants with:

- preferred body: **9 pt**;
- minimum body: **8.5 pt**;
- preferred title: **10 pt**;
- minimum title: **9.25 pt**.

Long headings grow the green title band to two or, exceptionally, three lines before any bounded
font reduction is attempted. The renderer no longer relies on very small 7.6–8.1 pt project copy.

### 3. Consistent right-hand imagery

Print / Compact no longer alternates photographs according to project ordinal. Single images
and Gallery 2 stacks use one consistent upper-right institutional grammar. Digital / Comfortable
retains its existing editorial layouts and alternation.

### 4. Geometry-aware paragraph alignment

The narrow leading text beside a photograph is left aligned to avoid stretched word spacing.
Only the full-width continuation is justified. Text-only project modules remain justified.

### 5. Stronger final institutional matter

The final-sheet treatment has been restored closer to the reference brochure:

- Visionary body: 10.4 pt;
- Visionary heading: 11.2 pt;
- New Simulators: 8.8 pt;
- closing strapline: 8.2 pt.

The measured page planner automatically reserves the larger closing block and continues to share
it with the final project sheet whenever the measured geometry permits.

### 6. Reference colour and first-page details

- Print project green moved to `#156656`, closer to the approved brochure.
- The central `CONTACTS` identifier is restored over the red agency panel.
- Cover A still uses authoritative institutional artwork when supplied.
- The fallback Cover A has been strengthened with a restrained SDD capability lock-up so a
  missing optional artwork asset no longer leaves a large visually inactive field.

## Files changed by Phase 10

- `Services/Publications/BrochureContracts.cs`
- `Services/Publications/BrochurePrintLayoutMetrics.cs`
- `Services/Publications/BrochurePrintMeasurementService.cs`
- `Utilities/Reporting/BrochurePrintCompactComposer.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs`
- `wwwroot/js/projects/publications-brochure-contract.test.js`
- `tools/Test-PrismPublicationsPhase10.ps1` (new)

There is **no EF migration**, **no Program.cs change**, **no CSS change**, and **no client workflow change**.

## Installation

This incremental package assumes the **Phase 9 measured-print implementation is already installed**.
That is the baseline represented by the current measured sheet-map/preflight implementation.

1. Stop PRISM / IIS Express if desired.
2. Copy the package contents over the `ProjectManagement` project root.
3. Preserve the directory structure and replace matching files.
4. Do not copy `README-REPLACE-FIRST.md` into the application if you do not want project-root notes.

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase10.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

## Acceptance checks

Use the same 8–12 representative test projects and **Print / Compact**.

Verify:

1. Project photographs always appear on the upper-right in hard-copy output.
2. Narrative copy visibly returns to full width below the image instead of leaving an empty
   column beneath the photograph.
3. Project body copy is visibly larger than the Phase 9 output; normal modules should render
   close to 9 pt and never below the 8.5 pt compact floor.
4. Long project titles wrap in a taller green band rather than becoming tiny.
5. Gallery 2 shows two right-hand images with the narrative wrapping beside the combined stack.
6. Narrow text beside imagery is not aggressively justified; the full-width continuation is.
7. Sheet planning remains order-preserving and the preflight sheet map remains operational.
8. Final Visionary / New Simulators matter is substantially more prominent and still shares the
   final project sheet where measured space permits.
9. Cover A uses institutional artwork when available; the fallback remains credible when it is not.
10. The red first-page agency panel shows the central `CONTACTS` identifier.
11. Switch to Digital / Comfortable and confirm its existing A4 renderer is unchanged.

## Validation completed in the preparation environment

- `node --check wwwroot/js/pages/projects-brochure.js` — passed.
- `node --test wwwroot/js/projects/publications-brochure-contract.test.js` — **32/32 passed**.
- Structural delimiter checks passed for all Phase 10 modified C# files.

The preparation environment does not expose the .NET SDK, so `dotnet build` and xUnit execution
must be run on the normal PRISM development machine before deployment.
