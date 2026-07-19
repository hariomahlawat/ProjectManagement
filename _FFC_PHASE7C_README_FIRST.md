# FFC Phase 7C — Global Footprint Slide Refinement

## Apply this package

Apply this focused package after **FFC Phase 7B — Native PowerPoint Tables**, including the Phase 7B build correction.

Extract the package into the `ProjectManagement` project root, preserve the folder structure, and replace the matching files.

**No database migration or NuGet package change is required.**

## Scope deliberately retained

The existing first **IPA and GSL status** slide is not changed. Its current shape-based layout is retained exactly as approved.

## Implemented

### Global Footprint slide

- Displays all 11 countries in the ranked country panel.
- Uses separate `COUNTRY` and `QTY` headings.
- Removes the repeated small `Qty` suffix from each row.
- Shortens formal comma-qualified country names only inside the compact ranked panel; full names remain unchanged elsewhere.
- Retains the existing map-and-ranked-list composition.

### PowerPoint map

- Country callouts now show ISO-3 codes only; quantities are not repeated on the map.
- Adds preferred placements for the crowded South Asia cluster and robust generic collision avoidance.
- Adds leader lines only when a label is displaced from its country.
- Adds a subtle border around label callouts for projection and print clarity.
- Uses four dynamically calculated quantity bands.
- For the current maximum quantity of 13, the bands are `1–3`, `4–6`, `7–9`, and `10–13`.
- Map fills and legend swatches now use the same discrete band colours.
- Legend ranges no longer repeat `Qty` because the legend heading already states `Total quantity`.

### Regression tests

- Verifies the current four-band range calculation.
- Verifies that the Global Footprint slide lists all 11 countries.
- Verifies that the panel does not fall back to `+ 2 more countries`.
- Verifies removal of the per-row `Qty` suffix.

## Files replaced

```text
Services/Ffc/Presentation/FfcSlideComposer.cs
Services/Ffc/Presentation/FfcPresentationMapRenderer.cs
ProjectManagement.Tests/ProjectOfficeReports/FfcSlideComposerTests.cs
ProjectManagement.Tests/ProjectOfficeReports/FfcPresentationMapRendererTests.cs
```

## Build and test

Run from the project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
```

## Manual acceptance checks

1. Generate a Full Portfolio deck.
2. Open the **Global footprint** slide.
3. Confirm all 11 countries are visible in the right-hand ranked list.
4. Confirm the list has `COUNTRY` and `QTY` headings and no small `Qty` suffix after each value.
5. Confirm map callouts contain ISO codes only.
6. Confirm Nepal, Bangladesh, Myanmar, Sri Lanka and Cambodia labels are separated and readable.
7. Confirm the legend shows four ranges when the maximum quantity is 13.
8. Confirm the first IPA/GSL status slide remains visually unchanged.
