# PRISM — Project Management ERP

PRISM is the institutional project-management and reporting system: an ASP.NET Core 8 Razor Pages
application on PostgreSQL. It covers the full project lifecycle (procurement stages, timelines,
documents, media, remarks), Project Office trackers (ToT, IPR, FFC, ARPP, proliferation, visits,
training, social media), publications and briefing decks, and supporting tools such as the notebook, calendar,
global search and the document repository. The application is deployed on Windows Server / IIS.

> The code is the authority. The guides under [`docs/`](docs/README.md) were re-verified against the
> code on 2026-09-27. The many `README-*`, `PHASE*`, `_FFC_PHASE*` and `*.patch` files in the repository
> root are historical delivery notes from individual change packages. They are **not** maintained and can
> contradict the current code; see [Repository hygiene](#repository-hygiene).

## Technology

| Area | Stack |
| --- | --- |
| Web | ASP.NET Core 8 (`net8.0`) Razor Pages, a few MVC API controllers and minimal APIs, SignalR (notifications) |
| Data | EF Core 8 + Npgsql 8.0.11 on PostgreSQL (CI uses 16). Two contexts in one database: `ApplicationDbContext` (115 migrations) and `MediaLibraryDbContext` (13 migrations, history table `__EFMigrationsHistory_MediaLibrary`). Requires the `pg_trgm` and `pgcrypto` extensions. Npgsql legacy timestamp behaviour is enabled. |
| Identity | ASP.NET Core Identity with cookie auth, 11 assignable roles (`Configuration/RoleNames`) and 60+ named policies registered in `Program.cs` |
| Documents | QuestPDF, SkiaSharp, PdfPig, ClosedXML, DocumentFormat.OpenXml (Word/PowerPoint), Markdig + HtmlSanitizer |
| OCR / search | External `ocrmypdf` process; PostgreSQL full-text search (weighted `tsvector`) and `pg_trgm`; Search V2 projection index |
| Media | ImageSharp; ONNX Runtime (YuNet/SFace face models, opt-in) |
| Front end | Static JS/CSS under `wwwroot`. The Notebook bundle is built with esbuild and **committed** in `wwwroot/dist`. |

## Modules

- **Projects:** repository, overview, procurement stages and timeline/plan approvals, documents, photos, videos, remarks, ToT, ongoing/completed views, archive/trash/purge, analytics.
- **Project Office Reports:** Visits, Social Media, Training, ToT tracker, Proliferation, IPR/patents, FFC, ARPP/PPP, Progress Review. Repeat-build projects are excluded from ToT and proliferation.
- **Publications:** Capability Brochure and Compendium PDFs built from live project data.
- **Briefing decks:** PowerPoint decks for Comdt/HoD built from `Resources/ProjectBriefing`, plus the FFC portfolio export.
- **Reports:** ARPP FY and FFC Projects Update exports (Word/PDF/Excel).
- **Workspaces and dashboard:** Command and Project Officer workspaces, conference review, Decision Centre (approvals), dashboard widgets.
- **Collaboration:** Notebook (notes/checklists with sharing), Tasks and Action Tracker (sprints), Calendar with celebrations and holidays, Notifications (SignalR).
- **Knowledge:** Global Search (V2), Document Repository with OCR, Photos/Media Library (albums, NAS sources, optional face recognition).
- **Directories:** Industry Partners, Project Ideas, Institutional Activities.
- **Administration:** users and lifecycle, access governance, audit and login logs, recovery, master data, maintenance, search index health, ERP usage analytics.

## Quick start (development)

Prerequisites: .NET 8 SDK, Node.js 22 + npm, PostgreSQL 16. The .NET build **fails** unless `npm ci` has been run,
because the csproj rebuilds the Notebook bundle.

```bash
npm ci
dotnet tool restore            # dotnet-ef 8.0.19
dotnet build
dotnet run --launch-profile ProjectManagement   # https://localhost:7183
```

- Set `ConnectionStrings__DefaultConnection` (or user secrets) for your database. All pending migrations for both contexts are
  applied **automatically at startup**, and startup stops if the migration lineage or schema is invalid (see [`MIGRATIONS-POLICY.md`](MIGRATIONS-POLICY.md)).
- To seed an empty database, set `Database:RunSeedersOnStartup=true` and `PRISM_BOOTSTRAP_ADMIN_PASSWORD` (or
  `Security:BootstrapAdminPassword`). This creates `admin`, who must change the password on first login.
- `appsettings.Development.json` points at Windows paths (`D:/ProjectManagementData/...` and `C:/Python311/Scripts/ocrmypdf.exe`).
  Override these off Windows, or the OCR options validator stops startup.

Full details, including tests and CI: [`docs/development.md`](docs/development.md).

## Deployment

Production runs as a self-contained `win-x64` publish under IIS (in-process, HTTPS required because cookies use the `__Host-` prefix).
Configuration comes from `appsettings.Production.json` (data under `F:/ProjectManagementData/...`) plus environment variables:
`ConnectionStrings__DefaultConnection`, `DP_KEYS_DIR` (data-protection keys; set it and back it up), `PM_UPLOAD_ROOT` (optional), and the `ocrmypdf` path.
See [`docs/deployment/offline-ws2022.md`](docs/deployment/offline-ws2022.md), [`docs/disaster-recovery.md`](docs/disaster-recovery.md) and
[`docs/configuration-reference.md`](docs/configuration-reference.md).

## Documentation

Start at [`docs/README.md`](docs/README.md). Architecture, data model, configuration, the page catalogue, module guides and
operations are all indexed there. The code review from the same audit is in [`docs/review-2026-09.md`](docs/review-2026-09.md).

## Repository hygiene

The repository root holds about 300 historical delivery artefacts: per-phase READMEs, `CHANGED-FILES`/`VALIDATION`
notes, `*.patch` files, `_PATCH/`, `_PACKAGE/` and `_tmp_probe_should_not_exist.txt`. None are read by the build, but they
bury the real documentation and several of them make claims that are now false (for example a `/health` endpoint, a single
migration lock, or "no credentials in appsettings"). The plan is to move them to an `archive/` folder or delete them;
until then, treat everything outside `docs/`, this README and `MIGRATIONS-POLICY.md` as history.
