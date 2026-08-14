# Phase 30.1 Ready-to-Paste

Baseline: corrected **Phase 30 — Cover Composer & Publication Imagery**, including the `BrochurePhotoService.cs` CS0136 hotfix.

Apply this package at the ProjectManagement root and overwrite matching files.

No new database migration is required.

After replacement:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase30_1.ps1
```

Primary checks in the browser:
1. Cover Editor Title/Subtitle/Edition show explicit inherited state and can Override / Reset to inherited.
2. Minimal cover hides Hero quick controls on the main Compendium page.
3. Editorial Split / Triptych use distinct automatic imagery where the selected project set provides alternatives.
4. Cover proof title wrapping and slot geometry closely match PDF Preview.
5. Formation and SDD marks remain optically balanced in Top corners and Top centre.
6. Fit disables Adjust crop; Fill enables it.
7. Missing Cover-suitable automatic imagery is a Warning, not Information.
