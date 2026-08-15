# PRISM Publications Phase 33 - Production Hardening

## Purpose

Phase 33 is a focused hardening pass for the Simulators Compendium after Phase 32 adaptive composition. It does not redesign the publication workflow and adds no database migration. It repairs the remaining interaction defects, aligns browser proof typography with the final PDF, removes narrow-control overflow, and protects the review workspace from the floating output dock.

This package also carries forward the Phase 32 `CompendiumDossierPaginationPlanner` CS1503 hotfix (`specifications.Length`).

## What is fixed

### 1. Cover Editor - Change image
The cover photo picker, crop modal and unsaved-change modal are Bootstrap body-level portals outside `data-compendium-cover-editor`. Phase 32 queried those controls through `root.querySelector`, so `Change` failed before the modal could open.

Phase 33 keeps normal editor queries scoped to the editor root and introduces an explicit document-level portal query for modal controls. Hero and supporting-image `Change` now reach the picker reliably.

### 2. Cover Editor - Adjust crop
A visible automatic image no longer has to be manually re-selected before cropping. When the slot is `Automatic + Fill`, clicking `Adjust crop` pins the currently resolved photograph as the explicit publication image, preserves the resolved preview, and opens the focal crop editor. `Fit` continues to disable crop because no crop is applied in Fit mode.

### 3. Back to Compendium hover
The previous heading CSS targeted every descendant `span`, including the text inside the outline Back button. The selector is now constrained to the heading metadata only, while the button label explicitly inherits the Bootstrap button colour. The same defensive correction is applied to the Structure Editor Back button.

### 4. Focus Review spacing
`Focus review` is now an inline-flex button with a controlled icon/text gap and non-collapsing children. The icon and label no longer touch.

### 5. Page Layout control overflow
The five dossier layout choices no longer compete in five equal narrow columns. The inspector uses a stable 3 + 2 arrangement on desktop and a two-column fallback on small containers. Buttons can wrap safely without overflowing their card.

### 6. Browser proof / PDF typography parity
The live dossier proof and cover proof now use the same local DM Sans publication family used by the server publication font service. `CompendiumPdfReportBuilder` now consumes `IPublicationFontService`, registers the publication fonts before composition, and applies the resolved primary family through the QuestPDF default text style.

The previous PDF fallback to Lato is therefore removed whenever the packaged DM Sans files are available. No font files are changed by this phase.

### 7. PDF tracking and heading rhythm
Excessive tracking was reduced for:
- section kickers;
- narrative headings;
- Programme Information;
- Hardware / Technical Specification;
- continuation labels;
- capability/reference micro-labels;
- cover markings and eyebrow text.

Project-title line height is also relaxed slightly to improve two-line title legibility.

### 8. Floating Final Output dock
When the fixed output dock is visible on desktop, the sticky right rail reserves a bottom safety area. The dock therefore occupies reserved rail space rather than obscuring lower publication controls. Focus Review still hides the compact rail as designed.

### 9. Review and PDF identity
The publication build identity advances to:

`CompendiumPdf_2026-08-15_production-hardening-v13`

The review fingerprint advances to:

`compendium-review-v8-production-hardening`

This deliberately invalidates old visual approvals after the presentation contract changes. Project facts are not changed.

## Ready-to-paste files

The ZIP is an overwrite package. Extract it into the **ProjectManagement project root** and allow matching files to be replaced.

No EF migration is required.

## Validation

Run from the ProjectManagement root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase33.ps1
```

The script performs JavaScript syntax checks, the complete Compendium contract suite, the Phase 32 planner hotfix check, the Phase 33 font/interaction contracts, and - where the .NET SDK is installed - `dotnet build` and `dotnet test`.

## Browser acceptance checks

1. Cover Editor -> choose an image-bearing template -> click `Change` on Hero and each supporting slot. The photograph picker must open every time.
2. Leave a slot Automatic + Fill -> click `Adjust crop`. The current automatic photograph should become the curated publication image and the crop editor should open directly.
3. Set the same slot to Fit. `Adjust crop` must become unavailable.
4. Move the focal point, Save, leave the editor, reopen it, and confirm image/crop persistence.
5. Hover `Back to Compendium` in both Cover and Structure editors. Icon and label must remain clearly visible.
6. Open Review at normal desktop width and Focus Review. The icon/label spacing must remain clean.
7. Inspect Page Layout at approximately 320-420 px inspector width. Automatic / Visual / Balanced / Multi-image / Technical must remain fully inside the card.
8. Scroll the Review inspector while the compact Final Output dock is present. Lower controls must remain reachable and not sit underneath the dock.
9. Generate Preview PDF. Compare `PROJECT BRIEF`, `PROGRAMME INFORMATION`, `HARDWARE / TECHNICAL SPECIFICATION`, section kickers and two-line project titles with the browser proof. Tracking should be materially tighter and font character shapes should match.
10. Complete review again (expected because fingerprint v8 intentionally invalidates previous visual approvals), generate the final PDF and verify normal text search/copy behavior.

## Preparation-environment validation

- JavaScript syntax: PASS
- Compendium Node contract tests: 127 / 127 PASS
- Phase 33 contract tests: 8 / 8 PASS
- CSS brace sanity: PASS
- .NET SDK is not installed in the preparation environment, so no local `dotnet build` or `dotnet test` result is claimed. The supplied PowerShell validator runs both on the development workstation.
