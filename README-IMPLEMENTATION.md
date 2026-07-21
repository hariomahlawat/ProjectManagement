# Simulators Compendium — Professional Hardening

This package is prepared as a **ready-to-replace implementation** for the PRISM ERP source tree.

## What this phase implements

### Publication preflight

- Shows eligible simulators, completed-project scope, category count and selected-photo coverage.
- Reports completed projects excluded because proliferation availability is disabled or not recorded.
- Detects and displays:
  - missing photographs;
  - missing Arm/Service;
  - missing or zero proliferation cost;
  - missing description;
  - missing completion year; and
  - likely `AI`/`Al` title-entry errors.
- Provides direct links to the project, photograph manager and completed-project editor where the user is authorised.
- Does not block generation when warnings remain; the PDF uses deliberate placeholders.

### Photograph selection and loading

- Uses the explicit project cover photograph when valid.
- Otherwise uses a photograph marked as cover.
- Otherwise selects the best available project photograph, preferring non-low-resolution images.
- Loads a PDF-friendly configured derivative first and falls back safely.
- Preserves cancellation and logs missing or unreadable derivative files rather than swallowing failures silently.

### PDF publication

- Institutional SDD cover with title, subtitle, as-on date and portfolio summary.
- Optional handling/classification marking repeated on the cover, page header and footer.
- Category index with clickable simulator names and printed page references.
- Repeating category/index headers when a category spans pages.
- Simulator pages with:
  - technical category;
  - completion year;
  - Arm/Service;
  - indicative proliferation cost in ₹ lakh;
  - project reference, where recorded;
  - cost remarks, where recorded;
  - project photograph or a deliberate no-photo placeholder; and
  - Markdown-formatted description.
- PDF document metadata.
- Standard ligatures disabled to improve searchable/copyable text for words such as `completion`, `visualisation` and `firing`.
- Professional PRISM footer and page numbering.

### Web UX

- Compact, professional readiness dashboard.
- Controlled export error message.
- Double-submit protection and generation progress state.
- Expandable warning table without permanent filters.
- Responsive and print-specific styling.

## Replacement files

Copy these paths over the corresponding project files:

```text
Configuration/CompendiumPdfOptions.cs
Pages/Projects/Compendium/Index.cshtml
Pages/Projects/Compendium/Index.cshtml.cs
Services/Compendiums/CompendiumDtos.cs
Services/Compendiums/ICompendiumExportService.cs
Services/Compendiums/CompendiumExportService.cs
Services/Compendiums/CompendiumReadService.cs
Utilities/Reporting/CompendiumPdfReportBuilder.cs
Utilities/Reporting/MarkdownPdfRenderer.cs
wwwroot/css/pages/projects-compendium.css
wwwroot/js/pages/projects-compendium.js
ProjectManagement.Tests/Compendiums/CompendiumPublicationTests.cs
```

## Configuration

Merge the supplied `appsettings.CompendiumPdf.fragment.json` into the root `appsettings.json`.
Do not replace the full application configuration file.

Recommended section:

```json
"CompendiumPdf": {
  "Title": "SDD Simulators Compendium",
  "Subtitle": "Available for Proliferation",
  "UnitDisplayName": "Simulator Development Division",
  "IssuerDisplayName": "Simulator Development Division",
  "FileNamePrefix": "SDD_Simulators_Compendium",
  "MiscCategoryNames": [ "Misc", "Miscellaneous" ],
  "CoverPhotoDerivativeKey": "md",
  "PreferredPhotoFormat": "jpg",
  "PreferWebp": false,
  "ShowMissingPhotoPlaceholder": true
}
```

## Service registration

No new registration is required. The existing registrations remain valid:

```csharp
builder.Services.AddScoped<ICompendiumReadService, CompendiumReadService>();
builder.Services.AddScoped<ICompendiumExportService, CompendiumExportService>();
builder.Services.AddScoped<ICompendiumPdfReportBuilder, CompendiumPdfReportBuilder>();
```

`IClock`, `IProjectPhotoService`, `IWebHostEnvironment` and typed loggers are already resolved through the existing application container.

## Database

- No database migration.
- No model/schema change.
- Existing completed-project, technical-status, production-cost and project-photo records are used.

## Build and verification

From the project directory:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then:

1. Open `/Projects/Compendium`.
2. Confirm the preflight counts and warning links.
3. Generate without a marking and verify the PDF.
4. Generate with an authorised marking and confirm it is repeated correctly.
5. Confirm photographs appear for projects with any usable project photograph, even when no explicit cover was selected.
6. Search the PDF for `completion`, `visualisation`, `firing` and `proliferation`.
7. Verify index links and printed page numbers.
8. Check a long Markdown description and a project with no photograph.

Use **Ctrl+F5** after deployment so the versioned CSS and JavaScript are refreshed.

## Preparation validation completed

- C# files parsed successfully with a C# syntax parser.
- JavaScript syntax check passed under Node.js.
- CSS parsed without syntax errors.
- Replacement paths and patch application were checked against the supplied source snapshot.

The preparation container did not have a functioning .NET SDK or NuGet network access, so the final `dotnet build` and runtime QuestPDF test must be run in Visual Studio/local build environment using the commands above.
