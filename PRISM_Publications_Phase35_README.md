# PRISM Publications Phase 35 - Programme Semantics and Icon Polish

## Outcome

Phase 35 corrects the source meaning of `Arms / Services`, replaces the vague rendered heading `PROGRAMME INFORMATION` with `PROJECT PARTICULARS`, and removes the nested coloured rectangles around programme icons. The resulting browser proof and generated PDF use a quieter, publication-grade treatment while preserving the established category colours and common vector family.

The change is cumulative with the Phase 34 icon system and includes the earlier `IReadOnlyList<string>.Count` pagination build fix.

## Authoritative data rule

The visible publication label remains **Arms / Services**. Its value is now sourced exclusively from:

`Project.SponsoringLineDirectorate.Name`

`Project.ArmService` is not read and is not used as a fallback anywhere in the Compendium pipeline.

| Surface | Phase 35 behavior |
|---|---|
| Candidate register | Availability and displayed value use Sponsoring Line Directorate. |
| Review workspace | Programme module and project review DTO use Sponsoring Line Directorate. |
| Readiness | Missing-value information uses `missingSponsoringLineDirectorate`. |
| Review fingerprint | Sponsoring Line Directorate is part of the canonical reviewed facts. |
| PDF export | `Arms / Services` receives `SponsoringLineDirectorateDisplay`. |
| Structure-editor payload | Uses the explicitly named `sponsoringLineDirectorate` property. |

When no Sponsoring Line Directorate is recorded, the candidate register shows `Not recorded`, the optional programme module is omitted, and readiness reports an informational finding. The system does not silently substitute a different project field.

## Final icon treatment

The four programme categories retain their semantic colours and the six local 24 x 24 SVG assets introduced in Phase 34:

- Arms / Services: maroon shield/person;
- Proliferation cost: green rupee;
- IPR Filed, Granted and Mixed: one gold document-and-seal family with state-specific treatment; and
- Technology transfer: blue transfer arrows.

The visible icon rectangles have been removed. Each icon now sits in an invisible fixed-width alignment column: 22 x 22 for layout, with 18 x 18 artwork. This preserves exact alignment without creating a second set of boxes inside the programme panel. The pale panel, subtle outer border and dark-green top rule are the sole enclosing furniture.

This is intentionally not a frameless or floating layout. The outer panel continues to group the four facts; only the visually redundant inner tiles are removed.

## Review and cache behavior

The generated-PDF identity advances to:

`CompendiumPdf_2026-08-15_programme-particulars-v17`

Browser icon URLs continue to carry `?v=v16` because the SVG artwork is unchanged. The v17 PDF identity ensures previously generated PDFs are rebuilt with the new `PROJECT PARTICULARS` heading.

The editorial-review fingerprint advances to:

`compendium-review-v10-sponsoring-line-directorate`

This review reset is intentional. Unlike Phase 34, Phase 35 corrects the authoritative fact included in the publication. A previously reviewed project must be reviewed again when its fingerprint is recalculated; silently preserving approval would certify the wrong source value.

## Ready-to-paste installation

The ZIP is an overwrite package. Extract it into the `ProjectManagement` project root and allow matching files to be replaced.

No Entity Framework migration is required.

After replacement:

1. Run the Phase 35 validator shown below.
2. Restart the application so the v16 build stamp and updated source pipeline are active.
3. Hard-refresh the Compendium page once (`Ctrl+F5`).
4. Re-open each selected project in Review and confirm it again.
5. Generate a new Preview PDF; an already downloaded PDF cannot update itself.
6. Verify that the value under `Arms / Services` matches the project's Sponsoring Line Directorate record.

## Validation

Run from the `ProjectManagement` project root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase35.ps1
```

The validator checks:

- JavaScript syntax for the Compendium, Structure Editor and Cover Editor;
- the complete Compendium JavaScript contract suite;
- strict Sponsoring Line Directorate sourcing with no `Project.ArmService` fallback;
- DTO, readiness, fingerprint, browser and PDF semantic continuity;
- the original `IReadOnlyList<string>.Count` pagination compile fix;
- SVG canvas, stroke and self-contained-vector constraints;
- removal of browser and PDF icon tiles while retaining the fixed alignment column;
- the v17 generated-PDF identity, `PROJECT PARTICULARS` browser/PDF parity, and v10 review identity; and
- `dotnet build` and `dotnet test` when the .NET SDK is available.

## Preparation-environment result

- Compendium JavaScript syntax: PASS
- Compendium Node contract tests: 137 / 137 PASS
- Phase 35 semantic and unboxed-icon contracts: 5 / 5 PASS
- Six SVG assets parsed and rendered successfully: PASS
- Source-wide legacy Compendium alias scan: PASS
- .NET SDK was not installed in the preparation environment, so no local `dotnet build` or `dotnet test` result is claimed. The supplied PowerShell validator runs both on the development workstation.

## Visual acceptance checklist

1. There is no coloured square, rectangle, border or shadow immediately around any programme icon.
2. All four icon positions align despite the icons having different silhouettes.
3. Maroon, green, gold and blue remain legible but subordinate to the programme values.
4. Filed, Granted and Mixed IPR states remain distinguishable by shape/state, not colour alone.
5. The outer `PROJECT PARTICULARS` panel is the only card-like container.
6. `Arms / Services` displays the Sponsoring Line Directorate value and never the separate Arm/Service project field.
7. Missing Sponsoring Line Directorate data is reported transparently rather than replaced by unrelated data.
8. At 100% PDF zoom and in print preview, the 18-point symbols are crisp and optically balanced with the value typography.
