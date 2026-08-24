# PRISM Compendium Phase 44 — Semantic Editorial Justification

## What this package does

Phase 44 makes the Compendium's **Justified** narrative setting behave consistently across the physical dossier instead of silently reverting Balanced side-column prose to Left alignment below the previous 245 pt width threshold.

The implementation deliberately keeps the existing Compendium pagination architecture intact:

- paragraph/sentence-safe narrative splitting remains unchanged;
- Skia/DM Sans physical measurement remains unchanged;
- QuestPDF remains the only PDF text compositor;
- headings and bullet/list blocks remain naturally left aligned;
- the last line of a justified paragraph remains natural through QuestPDF;
- Balanced side prose, below-image prose, full-width prose, continuation pages and Additional Note prose all honour the publisher's requested alignment;
- new unsaved/authored Compendiums default to **Justified**;
- existing saved presets retain their persisted alignment, including legacy Left-aligned presets;
- existing Left-aligned review fingerprints remain valid; existing Justified reviews are invalidated once because their physical output changes.

## Files

This package contains 17 ready-to-paste files. Copy the package contents over the project root while preserving the directory structure.

### Production files

1. `Pages/Projects/Publications/Compendium/Index.cshtml`
2. `Pages/Projects/Publications/Compendium/Index.cshtml.cs`
3. `Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs`
4. `Services/Compendiums/CompendiumDtos.cs`
5. `Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs`
6. `Services/Compendiums/CompendiumReviewFingerprint.cs`
7. `Services/Compendiums/ICompendiumExportService.cs`
8. `Services/Publications/CompendiumPresetContracts.cs`
9. `Utilities/Reporting/CompendiumBuildIdentity.cs`
10. `Utilities/Reporting/CompendiumPdfReportBuilder.cs`

### Test/contract files

11. `ProjectManagement.Tests/Publications/CompendiumPhase37CompositionTests.cs`
12. `ProjectManagement.Tests/Publications/CompendiumPhase41ProductionConvergenceTests.cs`
13. `ProjectManagement.Tests/Publications/CompendiumPhase44SemanticJustificationTests.cs` **(new)**
14. `wwwroot/js/projects/publications-compendium-phase37-contract.test.js`
15. `wwwroot/js/projects/publications-compendium-phase41-offline-runtime.test.js`
16. `wwwroot/js/projects/publications-compendium-phase43-cover-proof-parity.test.js`
17. `wwwroot/js/projects/publications-compendium-phase44-contract.test.js` **(new)**

## No EF migration is required

Do **not** create an Entity Framework migration for Phase 44. The Compendium schema already persists `DefaultNarrativeAlignment` and project-level `NarrativeAlignmentOverride`. This phase changes policy/default behaviour, not database shape.

## Important compatibility behaviour

### New Compendiums

A new unsaved/authored Compendium now starts with **Justified** as the publication default.

### Existing saved Compendiums

Existing presets continue to use their stored value. Legacy preset normalisation remains Left aligned where the old schema did not carry an alignment value.

### Review fingerprints

- Left-aligned dossiers keep the exact existing `compendium-review-v19-cover-identity` contract so previously reviewed Left output is not unnecessarily invalidated.
- Justified dossiers use `compendium-review-v20-semantic-justification`; an existing Justified review therefore becomes stale once, correctly requiring visual re-review because Phase 44 changes its physical composition.

## Recommended verification after pasting

From the solution/project directory on the development machine:

```powershell
dotnet build .\ProjectManagement.csproj -c Debug
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Debug --no-build
node --test (Get-ChildItem .\wwwroot\js\projects\publications-compendium*.test.js | ForEach-Object FullName)
```

If your Node dependencies have not been restored and you want to run the complete npm suite, restore them first using the repository's normal package workflow before `npm test`.

## Visual smoke test

Use one project with a reasonably long Project Brief and at least one image.

1. Select **Balanced** dossier layout.
2. Select **Justified** narrative alignment.
3. Check **Flow below image**:
   - prose beside the image is justified;
   - prose below the image is justified;
   - the transition occurs at the planner's existing semantic boundary;
   - headings and bullets remain left/natural.
4. Check **Side column**:
   - the side narrative now visibly honours Justified instead of silently reverting to Left.
5. Switch to **Left aligned**:
   - the same narrative remains ragged-right;
   - project membership and semantic narrative segmentation do not change.
6. Generate Preview PDF and compare the Live Page proof with the PDF.

## Verification performed in the supplied environment

The .NET SDK is not installed in the execution environment, so `dotnet build` and xUnit could not be executed here. The C# changes were statically checked for balanced delimiters and the new xUnit tests are included for execution on your development machine.

The complete Compendium JavaScript contract suite was executed after the final changes:

- **276 tests**
- **276 passed**
- **0 failed**

The full repository `npm test` was also attempted earlier, but it cannot be treated as a clean verification in this environment because unrelated tests require the missing `jsdom` dependency and there are unrelated existing failures. Use the commands above on the normal development machine for full solution verification.
