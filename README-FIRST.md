# PRISM Briefing Deck – Role & Charter UX Polish

Replace the files in this package using the exact project-relative paths.

## Included refinements

- Authorised default wording changed to:
  **Military Diplomacy — Develop simulators and projects for Friendly Foreign Countries (FFCs)**.
- Exact legacy PRISM defaults are upgraded during configuration normalisation; user-authored variants are preserved.
- Native browser `confirm()` / `alert()` dialogs are removed from the briefing-deck workflow.
- A reusable, accessible PRISM confirmation dialog now protects:
  - Role & Charter unsaved changes;
  - SDD Institutional Profile unsaved changes;
  - Deck Settings unsaved changes;
  - additional-slide removal;
  - bulk project removal;
  - explicit navigation and form submissions.
- Safe action receives initial focus; Escape keeps editing; backdrop clicks do not approve destructive actions.
- Role/Charter editor fields are wider and responsive, preventing lead-phrase truncation.
- Role panel is more compact, the Charter heading has proper separation, and Charter cards use content-aware height.
- Add Slide explains when every available singleton slide type has already been added.
- Removing and re-adding an additional slide continues to retain its deck-specific configuration.

## Deployment

1. Stop the running application/IIS site.
2. Back up the current project files.
3. Copy the packaged files over the matching project-relative paths.
4. In Visual Studio: **Clean Solution**, then **Rebuild Solution**.
5. Run the `ProjectBriefings` test suite.
6. Restart the application and refresh the browser with `Ctrl+F5`.

No database migration is required.
