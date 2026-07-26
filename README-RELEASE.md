# PRISM project portfolio — remaining issues fix

## Included fixes

- Makes the command header a real shared card surface in its Razor markup and
  removes styling dependence on a diagnostic data attribute.
- Keeps the command-header CSS in the primary portfolio stylesheet, adds
  concrete colour fallbacks, and preserves active, completed, and cancelled
  variants.
- Makes repository lifecycle text project-status-aware, so an unfinished
  stage on a cancelled project is shown as **Ceased**, never **In progress**.
- Prevents completed projects from advertising a next operational stage.
- Preserves the first unresolved `NotStarted` stage as **Current stage**, while
  using **Start** and **Current stage not started** for its action and schedule
  text.
- Uses historical/read-only wording in the lower lifecycle summary for
  completed and cancelled projects.
- Removes the late `auto-fit` grid override that stretched a single filtered
  project card across the repository.
- Fixes singular project-count grammar in both repository summaries.
- Gives cancelled projects a dedicated semantic badge instead of the neutral
  unknown-status treatment.
- Distinguishes the weighted **Overall record health** score from the
  nine-field **Core profile fields** checklist without changing either
  calculation.
- Makes terminal empty-history subtitles agree with their empty-state body.
- Retains the natural 4:3 landscape cover-photo presentation already present
  in the supplied source.

## Delivery contract

`Pages/Projects/Overview.cshtml`, the command-header partial, the primary
portfolio stylesheet, and their regression test are included together. This
prevents a copy-by-manifest deployment from leaving the running application on
an older overview view.

## Validation limits

The supplied environment does not contain the .NET SDK. CSS parsing, package
integrity checks, source-contract checks, and targeted static assertions were
run here. Run the full Release build and test suite on the development or CI
machine before production deployment.
