PRISM Publications — Phase 21.1
Cover Editor Final Hardening
================================

Purpose
-------
This is the final UI/UX hardening pass for the Brochure Builder cover editor. It does not
redesign the already-approved Digital or Print publication output.

Implemented
-----------
1. Cover text remains collapsed by default and now shows a compact summary such as:
   Front 6 visible · Back 3 visible.

2. Front and back cover line editors are full-width and no longer truncate ordinary values.

3. Non-destructive visibility controls were added for:
   - Front institution / kicker
   - Front publication descriptor
   - Back organisation line
   - Back closing strapline
   - Back edition line

   Hiding a line preserves its text. Showing it again restores the previous wording.

4. Existing title/subtitle/edition/strapline visibility remains intact.

5. Front kicker/descriptor visibility participates in the authoritative Cover B review
   fingerprint. Toggling either invalidates stale Cover B approval correctly.

6. Saved Brochures persist the five new visibility choices. Preset schema version is now 3.

7. Cross-profile Cover strapline and handling/classification marking remain single authoritative
   settings under Advanced publication settings so Print and Digital cannot diverge accidentally.
   The Cover Text section provides direct Edit strapline / Edit marking actions that open and
   focus the authoritative fields.

8. Additional Introduction heading is read-only/dimmed while the supplementary introduction body
   is empty. Its value is preserved and becomes editable immediately when body text is supplied.

9. A duplicate Cover B focal-point click listener was removed.

10. Renderer regression coverage now verifies that hidden lines with retained text are absent
    from both front and back cover PDF text.

Why the marking is not given an independent cover-only Show switch
------------------------------------------------------------------
A handling/classification marking is publication-wide, not decorative cover copy. If specified,
PRISM applies one authoritative value consistently to the cover and publication headers. Clearing
it removes it. Phase 21.1 deliberately avoids a state where the cover says one thing while inner
pages say another.

Migration
---------
20261208113000_AddBrochureCoverVisibilityControls

Adds five boolean visibility columns and advances BrochurePreset.SettingsSchemaVersion from 2 to 3.
Existing saved brochures retain their current visible output (all five new flags default true).

Local validation
----------------
Run:

  Set-ExecutionPolicy -Scope Process Bypass
  .\tools\Test-PrismPublicationsPhase21_1.ps1

Then perform the normal build/test cycle if not already executed by the script.

Recommended runtime checks
--------------------------
1. Hide front kicker and descriptor, preview Cover B, then show them again: text must return.
2. Hide all three back-cover text lines: back cover remains visually minimal, with no blank text placeholders.
3. Save the hidden-line configuration as a shared brochure, reload it, and confirm visibility is preserved.
4. Toggle front kicker/descriptor after Cover B approval: approval must reset.
5. Click Edit strapline / Edit marking from Cover Text: Advanced settings should open and focus the correct field.
6. With Additional introduction blank, the heading must be dim/read-only; entering body text enables it.
7. Run one Automatic Cover B preview and one Gallery 2 preview as final visual regression checks.
