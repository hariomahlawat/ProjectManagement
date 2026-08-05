# PRISM Briefing Deck — FFC Global Footprint

## Purpose
Adds **FFC Global Footprint** as the third registered Additional Slide type in the Project Briefing Deck Builder.

## Placement
The slide is fixed **immediately before the closing slide**. It cannot be dragged into the introductory slide sequence. The closing slide remains last.

## Authoritative data
The slide refreshes from the existing PRISM FFC module whenever PowerPoint is generated:

- countries;
- linked projects;
- installed quantity;
- delivered, awaiting installation;
- planned quantity;
- total quantity;
- latest FFC record update date.

The map is generated through the existing local FFC presentation-map renderer. No online map tiles, browser screenshot, or internet service is used.

## Slide composition
- Standard PRISM header and branding.
- Countries, Projects and Total Quantity KPIs.
- Installed / Delivered awaiting installation / Planned status bar.
- Local vector-derived footprint map rendered to the presentation.
- Configurable country-position list (6–10 countries).
- Standard footer with `Data as on ... · Source: PRISM ERP`.
- Editorial Light and Graphite Dark theme support.

## Workspace behaviour
- Available through **Additional slides → Add slide**.
- Registered as a singleton slide type.
- Displays a fixed-position pin and `Immediately before closing` summary.
- Dedicated focused configuration drawer.
- Removing and re-adding the slide preserves its configuration.
- Preflight and generated slide counts include the FFC slide.

## Application
Stop the running application and copy the project-relative files in this package over the corresponding files in the project.

Then perform:

1. **Clean Solution**
2. **Rebuild Solution**
3. Run the `ProjectBriefings` test suite
4. Start the application
5. Refresh the page with `Ctrl+F5`

## Database
No database migration is required. Configuration remains in the existing versioned briefing-deck JSON.

## Validation performed in the packaging environment
- JavaScript module syntax validation.
- Briefing-deck JavaScript suite: **32/32 passed**.
- C# changed-file lexical brace validation.
- Project and test project XML validation.
- Patch dry-run and clean-application byte comparison.
- ZIP integrity and SHA-256 verification.

The .NET SDK is unavailable in the packaging environment, so Visual Studio compilation is the final build verification.
