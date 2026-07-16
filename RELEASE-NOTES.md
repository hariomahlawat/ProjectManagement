# Release notes

## Corrected

- Loaded proliferation records now resolve Source reliably whether the API serialises the enum as a number, enum name or display label.
- Malformed years `0–99` are rendered exactly as stored; JavaScript no longer applies the implicit 1900 offset.
- Correction forms explicitly identify a suggested date derived from a two-digit stored year and require user verification.
- Pending approval and Counting rules now show one authoritative empty state instead of duplicated messages.
- Counting-rule actions are contextual:
  - no action without a valid project/source/year scope;
  - Save exception only for a non-default rule with a reason;
  - Restore source default only when an exception exists.
- Save and Correct record buttons have a visibly disabled state and accurate `aria-disabled` values.
- Correction reason is enforced by the client, DTO validation and the existing server-side service validation.
- Manage actions are visually integrated into the editor header, removing the redundant command layer.
- Empty list and exception footers are hidden when they contain no useful content.

## Preserved

- Approval and amendment rules.
- Admin/HoD automatic approval.
- Source defaults and project-year counting exceptions.
- Data-quality audit events.
- Existing APIs, database model and migrations.
