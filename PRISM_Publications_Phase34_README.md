# PRISM Publications Phase 34 - Programme Icon System

## Purpose

Phase 34 is a deliberately narrow visual-quality pass for the Compendium's `PROGRAMME INFORMATION` module. It replaces the remaining mixed icon treatment with one publication-specific vector family, gives Filed and Granted IPR states a clear shared grammar, and aligns the live browser proof with the generated PDF.

This phase does not change project text, project facts, selection, ordering, readiness policy, database schema, or the meaning of any programme field.

## Final visual system

All six icons are local SVG assets on the same 24 x 24 canvas. They use a 1.8-unit rounded line weight, one colour per category, no gradients, no filters, no embedded raster images, and no external icon or font dependency.

| Module/state | Visual | Colour | Rationale |
|---|---|---|---|
| Arms / Services | Shield with person | Maroon | Retains the defence/service meaning while removing filled clip-art detail. |
| Proliferation cost | Rupee in circle | Green | The strongest existing symbol; retained and normalized to the shared stroke. |
| Technology transfer | Opposing transfer arrows | Blue | Direct, compact and legible at print size; normalized to the shared stroke. |
| IPR - Filed | Document with hollow clock seal | Gold | Communicates a recorded application that remains in process. |
| IPR - Granted | Same document with solid seal and white check | Gold | Keeps the same IPR identity while making the completed state immediately visible. |
| IPR - Mixed | Same document with half-filled seal | Gold | Represents a portfolio containing both Filed and Granted records without inventing another colour. |

The previous award-like medal and unrelated green check are removed. State is now communicated through fill treatment inside a stable document-and-seal silhouette, not through a second icon family.

## Layout refinements

- Every module uses the same 22 x 22 near-square colour tile with a 2 px browser corner radius.
- The browser proof now includes the same `PROGRAMME INFORMATION` heading as the PDF.
- The programme panel's dark-green top rule is reduced from 3 px to 2 px in the browser and to 2.25 pt in the PDF. It remains an accent without overpowering the information.
- A single short programme module occupies half of the available row in both browser and PDF. A long value can still use the full width.
- The pagination estimate is adjusted by 0.75 pt to match the lighter PDF rule and preserve deterministic page planning.

## Cache and review behavior

The generated-PDF identity advances to:

`CompendiumPdf_2026-08-15_programme-iconography-v15`

Browser icon URLs carry `?v=v15`, so updated SVGs replace any cached v14 assets after deployment.

The editorial-review fingerprint intentionally remains:

`compendium-review-v9-programme-iconography`

This is a presentation-only refinement. Existing completed project reviews remain valid, while cached PDF output is regenerated with the new icon assets.

## Ready-to-paste installation

The ZIP is an overwrite package. Extract it into the `ProjectManagement` project root and allow matching files to be replaced.

No Entity Framework migration is required.

After deployment:

1. Restart the application so the v15 PDF build stamp and local SVG assets are active.
2. Hard-refresh the Compendium page once (`Ctrl+F5`).
3. Generate a new Preview PDF; an already downloaded PDF cannot update itself.
4. Compare Filed, Granted and mixed IPR examples at 100% PDF zoom and in print preview.

## Automated validation

Run from the `ProjectManagement` root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase34.ps1
```

The validator checks:

- JavaScript syntax for the Compendium, Structure Editor and Cover Editor;
- the complete Compendium JavaScript contract suite;
- SVG canvas, stroke and self-contained-vector constraints;
- the three-state IPR document/seal contract;
- browser/PDF layout parity;
- the v15 PDF cache identity and stable editorial-review identity; and
- `dotnet build` and `dotnet test` when the .NET SDK is available.

## Visual acceptance checklist

1. All programme tiles have the same size, corner treatment, inset and optical weight.
2. No icon looks like an emoji, illustration or separately sourced clip-art asset.
3. The Granted check remains visible at normal PDF zoom but does not introduce a green accent.
4. Filed reads as pending/in process; Granted reads as completed; Mixed remains visibly intermediate.
5. Arms / Services, Cost, IPR and Technology transfer remain distinguishable in grayscale by shape, not colour alone.
6. The programme heading, rule and value typography remain subordinate to the project title and narrative.
7. A one-module panel does not stretch an icon/value pair across the full page without need.
8. Pages that were one-page dossiers before this phase remain one-page dossiers after regeneration.

## Preparation-environment result

- JavaScript syntax: PASS
- Compendium Node contract tests: 132 / 132 PASS
- Phase 34 icon and parity contracts: 5 / 5 PASS
- Six SVG assets parsed and rendered successfully with Inkscape: PASS
- .NET SDK was not installed in the preparation environment, so no local `dotnet build` or `dotnet test` result is claimed. The supplied PowerShell validator runs both on the development workstation.

