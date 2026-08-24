# PRISM Brochure Builder — Editorial Alignment Phase

## What this package implements

- Adds **Project body alignment** with `Left aligned` and `Justified` choices.
- New, unsaved brochure sessions default to **Justified**.
- Existing saved brochure presets (schema 4 and earlier) load as **Left aligned** to preserve their historical output.
- Newly saved/updated presets use **schema 5** and persist the alignment choice.
- Print / Compact uses justified copy for:
  - text-only/full-width narrative;
  - narrative beside the upper-right image;
  - full-width narrative below the image.
- A forced mid-sentence float continuation remains ragged-right to prevent an artificially stretched first line.
- Digital / Comfortable honours the same brochure-level alignment choice.
- Review workspace reflects the selected alignment immediately.
- Alignment is durable preset state but is deliberately excluded from project/cover approval fingerprints, so changing typography does not revoke editorial approvals.
- Includes additive EF Core migration `20261216180000_AddBrochureNarrativeAlignment`.

## Paste / replace

Copy the folders in this package over the root of the existing `ProjectManagement` solution, preserving the directory structure. Files with the same path are complete replacement files; new files should be added as-is.

Do **not** create a second migration for this feature: the migration is already included.

## Verification on your development machine

From the solution/project root:

```powershell
node --check .\wwwroot\js\pages\projects-brochure.js
node --test --test-name-pattern="brochure editorial alignment|phase 16 semantic float composition" .\wwwroot\js\projects\publications-brochure-contract.test.js

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~BrochureNarrativeTypographyPolicyTests|FullyQualifiedName~BrochurePresetServiceTests"
```

Then run the normal publication regression suite used for PRISM and generate at least one Print / Compact and one Digital / Comfortable preview with both alignment modes.

## Database compatibility

The migration adds `BrochurePresets.NarrativeAlignment` with database default `Left` and changes the default schema version for new rows to 5. It deliberately does **not** rewrite existing preset rows to schema 5. This keeps existing saved brochures visually backward-compatible until a user explicitly changes/saves the new alignment setting.

## Recommended visual checks

1. Long project brief with one image: verify justified text beside the image and full-width below it.
2. Project whose image-height split falls mid-sentence: verify the forced continuation is not stretched.
3. Text-only project: verify full-width justification.
4. Existing saved preset: verify it initially opens Left aligned.
5. New unsaved builder: verify Justified is selected by default.
6. Toggle alignment on an already approved publication: verify project/cover approvals remain intact while preview/preflight refreshes.
