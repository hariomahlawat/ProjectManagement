# Release notes

## Release-blocking fix

`proliferation-manage.js` previously called `updateDecisionButtons()` before the `editor` and `saveButtonState` constants had been initialised. Browsers therefore stopped the script with a temporal-dead-zone `ReferenceError`. Initialisation now occurs in a deterministic order inside `init()`.

## Manage workflow

- Existing records load on page opening.
- Approval information is hidden for a new record and displayed only for a loaded record.
- New-record project selection starts blank and is mandatory.
- Detailed and annual record types use the same compact editor with contextual fields.
- New-record type controls and record actions are located with the editor rather than in separate full-width layers.
- Empty result areas no longer reserve excessive viewport height.

## Overview analytics

- Year-wise and Technical category are first-class Analytics tabs.
- The category chart is no longer concealed under the vague “Additional analysis” disclosure.
- Chart height, recent-year rows and surrounding card spacing are reduced.

## Data quality

- The red navigation badge represents malformed records requiring correction.
- Possible duplicates remain review items within the Data quality workspace and are not treated as equivalent to invalid dates, years, units or quantities.

## Reports and density

- The default report is represented by a compact selected-report strip.
- Report choices reopen through Change report.
- Records filters, KPI cards, project-year disclosures, report choices and Manage controls use a denser PRISM-aligned rhythm.
