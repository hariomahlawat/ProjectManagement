# Legacy training entry

**Status: implemented.** These were originally proposed wireframes. The feature now exists in the training form at `Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml` (`ManageModel`, route `/ProjectOfficeReports/Training/Manage/{id?}`). Only users who pass `ProjectOfficeReportsPolicies.ManageTrainingTracker` (Admin, HoD, Project Office) can open it. This page describes what was actually built.

## Header

The top card of the form has three controls:
- **Training type**: a dropdown.
- **Schedule mode**: radio buttons, either *Exact dates* or *Month & year only*.
- **Legacy record**: a form switch (not a plain checkbox), bound to `Input.IsLegacyRecord`, with the helper text "Use when migrating totals from historical registers."

```
┌──────────────────────────────────────────────────────────────┐
│ Training type [ v ]   Schedule mode (•) Exact dates           │
│                                     ( ) Month & year only     │
│                       Legacy record [switch]                  │
│                       "Use when migrating totals from         │
│                        historical registers."                 │
└──────────────────────────────────────────────────────────────┘
```

## When Legacy record is on

- Inputs for the officer, JCO and OR counts are shown (`Input.LegacyOfficerCount`, `Input.LegacyJcoCount`, `Input.LegacyOrCount`).
- An info hint explains: "Legacy records capture attendee totals without maintaining a roster. You can re-enable the roster at any time by unchecking the option above."
- When the form is saved, `ManageModel.OnPostSaveAsync` discards any roster rows, sets `HasRoster = false`, and records the counter source as `TrainingCounterSource.Legacy`.

## When Legacy record is off (default)

- The **Roster** card is shown. It includes a summary of officer, JCO and OR counts and a source label: *Roster* or *Legacy counts*.
- The roster grid (`_RosterGrid.cshtml`) is posted as `Input.RosterPayload`.
