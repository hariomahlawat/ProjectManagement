PRISM GLOBAL SEARCH V2 — CONVERGENCE / RUNTIME STABILIZATION

BASELINE
This overlay is designed for the immediately preceding Search V2 Relevance & Quality Hardening implementation.

HOW TO APPLY
1. Back up the project.
2. Copy every file/folder from this package over the project root, preserving relative paths.
3. No EF migration is required. ProjectionVersion remains 4.
4. Run: dotnet restore
5. Run: dotnet build
6. Run: dotnet test ProjectManagement.Tests
7. Start in Development and verify a committed query shows Engine: V2.
8. Test aura, high tech, high-tech, HI-TECH and hyderabad.

If a committed query shows Legacy fallback in Development, note its diagnostic ID and inspect the server log entry carrying the same ID. Exception details are deliberately not rendered in the browser.

See README-SEARCH-V2-RUNTIME-STABILIZATION.md for full details.
