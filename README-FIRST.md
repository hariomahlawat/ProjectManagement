# PRISM Briefing Deck — FFC Global Footprint Design Polish

## Purpose
Cumulative ready-to-replace package for the Additional Slides framework, Role & Charter slide, and FFC Global Footprint slide, including the final FFC design and workflow refinements.

## Refinements in this phase
- Additional Slides workspace is divided into **Opening slides** and **Before closing** placement zones.
- FFC Global Footprint no longer displays a misleading opening-slide sequence number or drag control.
- FFC remains fixed immediately before the closing/Jai Hind slide.
- Slide Library wording now states that each **approved slide type** can be added once.
- FFC editor preview displays current live Countries, Projects and Total Quantity values.
- FFC preview failure is isolated and logged without preventing the deck page from loading.
- FFC footer uses the PowerPoint generation date: `Data as on dd MMM yyyy · Source: PRISM ERP`.
- Map rendering uses a dedicated tighter active-country viewport and larger map labels for briefing slides.
- Main slide geometry gives the map an aspect-correct panel and the country-position list more width.
- Country-position heading now reads `TOTAL QTY`.
- 9–10 country lists use a controlled compact row mode without shrinking the text.
- Overflow text has additional lower clearance.

## Data and placement
All FFC figures remain authoritative and read-only, sourced from the existing PRISM FFC module whenever PowerPoint is generated. The slide remains the final substantive slide immediately before the closing slide.

## Application
Stop the running application and copy the project-relative files in this package over the corresponding project files.

Then perform:
1. Clean Solution
2. Rebuild Solution
3. Run the ProjectBriefings and FFC presentation tests
4. Start the application
5. Refresh the browser with Ctrl+F5

## Database
No database migration is required.

## Validation performed
- Briefing-deck JavaScript tests: 32/32 passed.
- JavaScript module syntax validation passed.
- C# changed-file lexical brace validation passed.
- Project and test-project XML validation passed.
- Patch dry-run and clean-application comparison passed.
- ZIP integrity and SHA-256 validation passed.

The .NET SDK is unavailable in the packaging environment, so Visual Studio compilation is the final build verification.
