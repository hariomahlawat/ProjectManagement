PRISM Notebook — Conference Digest Typography Refinement
Date: 09 Aug 2026

Purpose
-------
Reduces the visual weight of the Command / Latest Conference Directions digest without changing its layout, data, grouping, navigation, or read-only behaviour.

Changes
-------
- PO names remain the primary content anchor at font-weight 600.
- Project / Idea / Task names are reduced to medium weight (500).
- Conference direction text remains regular weight (400).
- Date/time metadata is regular weight (400 in the expanded register; 500 in the compact preview).
- Conference Review links and secondary metadata are reduced to 500.
- Main modal title is retained at 650 and the small system eyebrow at 600.
- Compact-card title is retained at 600.
- Count badge remains 700 because it is a small isolated numeric indicator.
- No spacing, divider, modal, card, query, model, database, authorization, or conference logic changes.

Files
-----
Replace:
  wwwroot/css/notebook.css

Optional regression test to add:
  wwwroot/js/notebook/notebook-conference-digest-typography-contract.test.js

Validation
----------
Focused Conference Digest contract tests: 9/9 passed.
No EF migration is required.
No production JavaScript was changed, so no bundle rebuild is required solely for this change.
A normal .NET build / hard browser refresh is sufficient after copying the CSS.
