PRISM — Completed Projects latest-completion ordering
Generated: 29 Jul 2026

PURPOSE
The Completed Projects workspace now opens in completion chronology, latest first,
instead of alphabetical project-name order.

APPLY
1. Extract this ZIP into the ProjectManagement project root.
2. Preserve the folder structure and replace the existing files when prompted.
3. Build and test:

   dotnet build
   dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

IMPLEMENTED RULES
- Default sort: Completed, descending.
- Exact dates are ordered by year, month and day.
- Month-and-year records are ordered by recorded year and month.
- Year-only records are ordered by recorded year.
- Within the same year/month, more precise records appear before less precise records.
- Projects with no completion information remain at the end.
- Project name and Project ID provide deterministic final tie-breakers.
- Existing URLs using Sort=year remain supported and are normalised to Sort=completed.
- The register, overview queues and Excel export use the same completion chronology.
- The Completed column now shows the recorded precision: dd MMM yyyy, MMM yyyy or yyyy.

DATABASE
No migration is required.

FILES
Eight project/test files are included. See CHANGED-FILES.txt.
