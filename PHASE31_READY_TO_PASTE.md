# Phase 31 — Ready to Paste

Apply this package over the corrected **Phase 30.1** ProjectManagement source.

## Required database step
Phase 31 includes a migration:

`Migrations/20261208190000_AddCompendiumAdaptiveDossiers.cs`

After replacing the files, run the normal application migration/startup path or:

```powershell
dotnet ef database update
```

Then validate:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase31.ps1
```

Phase 31 introduces four adaptive project dossier families, up to three publication image slots per project, modular Sponsoring Line Directorate / Proliferation Cost / IPR / ToT presentation, and authoritative 1–6 item Hardware / Technical Specification project data.
