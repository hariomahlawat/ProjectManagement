# PRISM Compendium Phase 42 — Ready to Paste

This is a cumulative replacement package for the supplied `ProjectManagement-master` source. It includes the Phase 41 air-gapped IIS/PDF production fixes and the Phase 42 slot-stable Cover Editor implementation.

## Result

Cover image slots are independent publication state.

- Changing **Supporting image 1** changes only Supporting image 1.
- `Hero=A, Supporting 1=B, Supporting 2=C` followed by a manual Supporting 1 selection of `D` becomes exactly `A / D / C`.
- Automatic slots retain their resolved project/photo identity across save, reload, readiness, preview and final PDF generation.
- A stale or unavailable automatic photograph releases only its own slot.
- Manual assignments always win and are never consumed by automatic fallback.
- Portfolio Quartet remains Fill-only and requires four different usable photographs.
- Front and back cover allocation remain independent.
- **Refresh automatic** is the only command that intentionally re-ranks all visible automatic slots on the current cover surface.

## Apply

1. Back up the source tree and database.
2. Copy the contents of this folder into the project root, preserving the supplied relative paths and replacing existing files.
3. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\Test-PrismPublicationsPhase42.ps1
   ```

4. Create and validate the offline Windows payload:

   ```powershell
   .\ops\publish\create-publish-folder.ps1
   ```

5. Deploy the validated `artifacts\publish\ProjectManagement` folder to IIS following `docs\deployment\offline-ws2022.md`.

## Database impact

No database migration is required. Phase 42 stores automatic slot identity in the existing cover-image `ProjectId` and `PhotoId` columns.

## Important deployment rule

Publish the complete self-contained `win-x64` output. Do not copy only the application DLL to the offline IIS server. The validated payload must include the .NET runtime, SkiaSharp native binary, QuestPDF dependencies and all six local DM Sans publication fonts.
