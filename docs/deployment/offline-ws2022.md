# Offline Deployment and Production Readiness for Windows Server 2022 (IIS)

These notes capture the production hardening and offline deployment standard for **ProjectManagement** when running on **Windows Server 2022** in an air-gapped or LAN-only environment using a **self-contained .NET 8 publish**. Follow this guide to avoid post-deployment surprises and to keep updates routine and safe.

## A. Production readiness checklist

### A1. Configuration hygiene

- Maintain three appsettings files:
  - `appsettings.json` (base)
  - `appsettings.Development.json` (no secrets)
  - `appsettings.Production.json` (offline server values)
- Production values must override all server-specific settings: database host, storage paths, OCR paths, external URLs, upload limits, scheduled intervals, and search/OCR tuning.
- No hard-coded environment values: connection strings, file paths, and feature flags must come from configuration binding (`IOptions<>` or `builder.Configuration`).
- Keep hierarchical key naming intact so overrides via environment variables (double underscore) remain possible.
- Ensure production defaults cover:
  - `ConnectionStrings:DefaultConnection`
  - Document repository root path (outside site root)
  - OCR worker settings, temp paths, and queue sizes
  - Search settings (language, ranking weights)
  - Upload size limits and allowed extensions
  - Hosted service intervals

### A2. IIS hosting requirements

- Even with a self-contained publish, IIS requires **ASP.NET Core Module v2 (ANCM)** provided by the **.NET 8 Hosting Bundle** to process `<aspNetCore>` in `web.config`.
- Publish output must include a clean `web.config` like:

  ```xml
  <configuration>
    <location path="." inheritInChildApplications="false">
      <system.webServer>
        <handlers>
          <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
        </handlers>
        <aspNetCore processPath=".\ProjectManagement.exe"
                    hostingModel="OutOfProcess"
                    stdoutLogEnabled="false"
                    stdoutLogFile=".\logs\stdout" />
      </system.webServer>
    </location>
  </configuration>
  ```

- Ensure `processPath` exactly matches the executable name.
- If the Hosting Bundle was installed before IIS, re-run or repair the installer after IIS is enabled.

### A3. Database and migrations discipline

- Do **not** auto-apply migrations in production. Apply reviewed SQL scripts instead.
- Deliver with each release:
  - `migration.sql` generated via `dotnet ef migrations script -i -o migration.sql`.
  - A short migration impact note summarizing schema changes and any data-destructive operations.
- Avoid drops/renames without data-copy strategy and explicit approval.

### A4. File storage separation

- Keep all persistent files outside the site root, e.g.:
  - `D:\PMData\Uploads`
  - `D:\PMData\Documents`
  - `D:\PMData\OcrCache`
  - `D:\PMData\Logs`
- Point appsettings to these external paths so IIS site folder contains only binaries and static assets.
- Refactor any module still writing inside the site root.

### A5. Logging and diagnostics

- Use offline-friendly logging to disk; ensure external log folder is writable by the IIS app pool identity.
- Keep stdout logging disabled by default; document how to toggle it quickly for troubleshooting.
- Provide a `/health` endpoint that checks:
  - Database connectivity
  - OCR tool availability
  - Storage path access
  - Search/index readiness

### A6. External dependency audit

- For air-gapped servers, every runtime dependency must be pre-installed or shipped with the app. List and validate:
  - Tesseract OCR runtime and language packs (exact folder path)
  - PDF/OCR helpers (e.g., ocrmypdf, poppler, ghostscript) if used
  - Native libraries for image/video
  - NuGet packages with outbound calls
- Produce a dependency map with version, installer location, and a test command.

### A7. Security and LAN-only posture

- Remove or guard any code path that reaches public URLs, update feeds, telemetry, analytics, or CDN assets.
- Serve all static assets locally (no CDN).
- Confirm production authentication/roles do not rely on external identity providers.

## B. Release package contents (per update)

Include a versioned folder containing:

1. `/publish/` self-contained output (`win-x64`, `net8.0`)
2. `migration.sql` (if schema changed)
3. `ReleaseNotes.md` with version, date, functional changes, config key deltas, migration impact summary, and rollback notes
4. `DeploymentChecklist.md` referencing the deployment steps below

## C. Offline deployment guide (Windows Server 2022 + IIS)

### C1. One-time server preparation

1. Install IIS role/features: Web Server (IIS), Application Development (ASP.NET, .NET Extensibility, ISAPI Extensions/Filters), and security options such as Windows Authentication as needed.
2. Install the **.NET 8 Hosting Bundle** offline, then restart IIS (required for ANCM v2).
3. Install PostgreSQL offline on a LAN host, configuring `listen_addresses` and `pg_hba.conf` for the subnet.
4. Create external data folders: `D:\PMData\Uploads`, `D:\PMData\Documents`, `D:\PMData\OcrCache`, `D:\PMData\Logs` with NTFS permissions: `IIS_IUSRS` read/write on data folders, read/execute on site root.

### C2. Publish command (dev machine)

```powershell
dotnet publish .\ProjectManagement.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishTrimmed=false -o .\publish
```

### C3. IIS site setup

1. Copy publish folder to `D:\Sites\ProjectManagement\current`.
2. Create App Pool `ProjectManagementPool` with **No Managed Code**, Integrated pipeline.
3. Create Site `ProjectManagement` pointing to `D:\Sites\ProjectManagement\current`; bind `http` on the chosen port (e.g., 8080).
4. Open firewall inbound TCP port (e.g., 8080) for Domain/Private profiles.
5. Set IIS environment variable: `ASPNETCORE_ENVIRONMENT = Production`.

### C4. First deployment flow

1. Verify database connectivity.
2. Apply `migration.sql` if present using `psql -h <db-ip> -U pm_user -d ProjectManagement -f migration.sql`.
3. Browse locally: `http://localhost:8080/`.
4. Browse from LAN: `http://<server-ip>:8080/`.
5. Hit `/health` and confirm green.

## D. Offline update and rollback SOP

### D1. Update steps

1. Take a database backup.
2. Place `app_offline.htm` into `current` for graceful stop.
3. Apply `migration.sql` (after review) if included.
4. Replace `current` binaries with new publish output.
5. Remove `app_offline.htm`.
6. Smoke test: homepage, login/roles, document upload + OCR status, global search for known doc, `/health`.

### D2. Rollback steps

1. Place `app_offline.htm` to stop the app.
2. Restore previous publish folder (kept under `releases\previous`).
3. Remove `app_offline.htm`.
4. If rollback crosses a migration boundary, restore DB backup or run reverse migration plan.

## E. Ownership and immediate actions

1. Configuration audit: ensure all server-dependent values live in `appsettings.Production.json`.
2. Persistent storage audit: confirm no module writes inside the IIS site root.
3. Dependency map: produce full offline dependency list for OCR and document processing.
4. Migration discipline: enforce scripted migrations and impact notes.
5. Health endpoint: implement and document.
6. Deployment docs: keep this SOP under `/docs/deployment/offline-ws2022.md`.

