# Proliferation Reports — next-phase correction (v3)

Apply this bundle over the simplified Proliferation Reports v2 implementation.

## What this corrects

- Authoritative totals no longer query unit-wise granular data during ordinary report generation.
- Unit-wise rows are fetched only after **Show unit-wise breakdown** or during Excel export.
- A failure in the supplementary unit query can no longer block the main total report.
- User-correctable validation errors are separated from unexpected server/EF errors.
- Unexpected failures are logged server-side and returned as safe `ProblemDetails` with a trace reference.
- Both supported antiforgery header names are sent for JSON POST requests.
- HTTP 400/401/403/404/405/500 responses now have controlled, useful messages.
- The Bootstrap `legend` float is reset, correcting `Select scopeSimulators`.
- Long simulator chips have controlled width and ellipsis.
- Receiving-unit counts are shown only after unit data has actually been loaded.

## Replace

Copy the included `Areas` and `wwwroot` folders into the project root and replace the matching files.

This bundle intentionally does not replace `Program.cs`. Confirm these registrations remain present:

```csharp
builder.Services.AddScoped<ProliferationAnalysisService>();
builder.Services.AddScoped<ProliferationAnalysisExcelBuilder>();
```

## Build

Stop the running application before replacing JavaScript/static files.

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then reload Reports with `Ctrl+F5`.

## Verification

1. Generate **All proliferation / All time**.
2. Generate a **Single year** report.
3. Generate a **Technical category** report.
4. Generate a **Selected simulators** report with multiple simulators.
5. Open **Show unit-wise breakdown** only after the total report succeeds.
6. Export Excel.

If an unexpected server error remains, the browser now displays a reference such as `Reference: 0HN...`. Use that value to locate the full exception in the application log without exposing diagnostics in the UI.

No database migration or package change is required.
