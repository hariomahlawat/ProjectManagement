# PRISM Project Briefing Deck — Adaptive Update-Sheet Layouts

## Replace
Copy the included files over the same relative paths in the PRISM project.

## Implemented behaviour

- **Compact layout:** automatically used when 1–2 information rows are rendered.
  - Full-width information band below the title.
  - Photograph and project brief use balanced side-by-side panels.
  - The photo/brief split adapts moderately to project-brief length.
- **Standard layout:** automatically used for the normal 3–5-row case.
  - Retains the established facts-left, photograph-right and brief-below design.
- **Detailed layout:** automatically used for 6–9 rows, or when actual row content needs additional height.
  - Wider facts table, narrower photograph column and bounded brief area.
- Layout selection is automatic. The user continues to select information only.
- **Recommended defaults** now selects the compact five-row command-update set:
  1. Project cost
  2. AoN date
  3. Supply-order date and firm
  4. PDC / completion status
  5. Present status
- **Select all** continues to select all nine available rows.
- Existing saved decks retain their saved row selection and order.
- No database migration or configuration change is required.

## Validation performed

- JavaScript syntax validated with Node.js `--check`.
- Modified C# files passed structural delimiter and string/comment-state checks.
- Regression tests were updated for the three layout variants and recommended-row behaviour.
- Run Clean/Rebuild and the ProjectBriefings test suite in Visual Studio before deployment; the .NET SDK was not available in the packaging environment.
