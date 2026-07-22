# Project Briefing Deck — Maturity Order and Table Readability

## Apply

Run from the extracted package directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\APPLY-FIX.ps1 `
  -ProjectRoot "E:\Dot Net Web Development\ProjectManagement"
```

The script backs up the affected files, applies the payload to the correct project paths, removes stale `bin`/`obj` outputs, and verifies every copied file by SHA-256.

## What changes

### One canonical project sequence

The same maturity sequence now controls:

- selected-project table in the builder;
- stage-summary chart and native stage table;
- PowerPoint executive status tables;
- detailed project slides and their continuation slides.

Sequence:

1. Completed
2. Transfer of Technology / Payment / Acceptance Testing, when still recorded as active closure stages
3. Development
4. Supply Order
5. EAS Approval
6. PNC
7. Commercial Bid Opening
8. Benchmarking
9. Technical Evaluation
10. Bidding / Tendering
11. Acceptance of Necessity
12. SOW Vetting
13. In-Principle Approval
14. Feasibility Study
15. Unmapped stages

The saved manual order remains the secondary order **within the same present stage**.

### Reordering behaviour

- Drag-and-drop and keyboard movement are limited to projects in the same stage.
- Bulk controls now move selected projects to the top or bottom of their respective stage groups.
- Reordering is disabled while filters are active.
- Cross-stage movement cannot silently break the maturity sequence.

### Table readability

- PowerPoint native-table horizontal cell margins increased to `0.11 in`.
- Vertical cell margins set to `0.04 in`.
- Executive table column widths rebalanced.
- Cost values are right-aligned.
- Project tables use six preferred rows per slide rather than seven; additional slides are created instead of shrinking content.
- Web-table Project and Status columns receive larger horizontal padding.

### Other rectifications included

- The selected-project table header no longer floats midway through the viewport; only the search/filter toolbar remains sticky.
- Editorial Light now uses a genuinely light cover canvas.
- Stage filter options follow maturity order instead of alphabetical order.

## Database

No migration and no database change are required.

## Validation completed here

- JavaScript syntax check: passed.
- Briefing-deck JavaScript tests: 10/10 passed.
- Changed C# files: structural delimiter/string scan passed.
- Replacement package file hashes: verified during packaging.

The .NET SDK is not installed in this execution environment, so `dotnet build` and the xUnit suite could not be executed here. Rebuild the solution locally after applying the package.
