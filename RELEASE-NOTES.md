# Release Notes — Proliferation PRISM Visual Alignment

## Release objective

Make the proliferation module look and behave like the wider PRISM ERP rather than a standalone analytics portal, without altering proliferation data, calculations or authority rules.

## Overview

- Normalised page titles and subtitles to the same scale used by PRISM administrative and operational pages.
- Removed redundant module eyebrows from Overview, Records, Reports, Manage and Project Total.
- Compacted the module navigation and page-action groups.
- Replaced the tall data-quality banner with a concise operational warning.
- Reduced project-finder height and changed the clear action to an icon control.
- Reduced KPI card height while retaining total-first hierarchy.
- Reduced Project totals table row height and removed the boxed chevron button.
- Reduced trend and category chart heights.
- Added exact technical-category values at the end of horizontal bars.
- Expanded common abbreviated category labels in the chart where safe.

## Project Total

- Replaced the large blue hero with a standard PRISM information card.
- Retained total proliferation as the dominant number using PRISM blue rather than a full-card gradient.
- Presented SDD, 515 ABW and valid-year metrics in a compact stat strip.
- Reduced project action and metadata spacing.
- Rebuilt collapsed project-year rows as compact one-line disclosures.
- Placed the disclosure chevron at the far right.
- Simplified annual-only calculations to show counted and reference quantities without repeating the reported result in a third large card.
- Retained the full annual + detailed = reported equation where both quantities are counted.

## Records

- Reduced filter-panel, result-card and grouped-row spacing.
- Preserved the project picker, source/year filtering, underlying-record export and lazy entry loading.
- Kept reported total visually stronger than annual and detailed quantities without oversized cards.

## Reports

- Reduced report-tile height, icon size and internal spacing.
- Compacted the project-total finder, report options, advanced filters and results.
- Preserved task-based report selection and selected-report collapse behaviour.

## Manage

- Renamed the page heading to `Manage proliferation` to reflect records, approvals, counting rules and data quality.
- Compacted workspace tabs, command context, new-entry actions, list rows, editor surfaces and rule/data-quality controls.
- Removed the Bootstrap `shadow-sm` override from the record list so it uses the module's standard PRISM surface treatment.
- Preserved contextual actions, amendment handling, approval impact, rejection reasons and auditable data correction.

## Technical notes

- No migration required.
- No API contract or proliferation calculation changed in this phase.
- Chart.js uses the application's body font and normalised 12 px chart labels.
- Technical-category chart height is calculated from the category count.
- The package is cumulative across the earlier proliferation UX, corrective and operational-integrity releases.
