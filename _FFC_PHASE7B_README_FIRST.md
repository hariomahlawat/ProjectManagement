# FFC Phase 7B — Native PowerPoint Tables

Apply this focused package over the existing Phase 7A implementation.

## Files replaced

```text
Services/Ffc/Presentation/FfcSlideComposer.cs
```

## What changed

This phase keeps the existing FFC PowerPoint content, slide order, branding, appendix density and data-quality behaviour, but refactors tabular briefing slides to use native editable PowerPoint tables.

Converted to native DrawingML PowerPoint tables:

- IPA and GSL status slides
- Country project-status slides
- Attachment-register slides
- Consolidated appendix slides

Retained as controlled visual shapes/images:

- Cover slide
- Portfolio-at-a-glance KPI tiles
- Quantity-status bars
- Global footprint map
- Country summary cards
- Operational-focus blocks
- Headers, footers and slide numbers

## User benefit

PowerPoint users can now select each generated table as a table object and use normal PowerPoint table operations such as editing cell text, resizing columns, changing alignment, and copying the table into another briefing.

## No schema change

No database migration is required.

## Verification after replacement

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
```

Generate a Full Portfolio PowerPoint and verify that the appendix and project-status slides are editable as standard PowerPoint tables, not as hundreds of independent shapes.
