PRISM ERP - Notebook System-Shared Conference Digest
09 Aug 2026

PURPOSE
-------
Moves the live Latest Conference Directions digest out of All Notes and into
Shared with me, where it is treated as a PRISM-shared, read-only virtual note.

UX RESULT
---------
1. All Notes is again reserved for normal Notebook content. The system digest no
   longer occupies the top of the personal Notebook canvas or pushes notes down.
2. Shared with me contains two provenance-aware sections when applicable:
      - From PRISM
      - From people
3. The Conference digest uses normal Notebook card width, border, radius, hover
   behaviour and typography instead of a special double-width card.
4. The card is marked subtly as "PRISM · Read only" and opens the existing full
   PO-wise Conference register.
5. The Shared with me rail count includes the digest as ONE shared surface, not
   as N conference directions. Example: 2 person-shared notes + digest = 3.
6. The digest remains virtual/live. It is not persisted as NotebookItem and is
   not pinnable, reorderable, editable, archivable, shareable or trashable.
7. Only Comdt/HoD users with at least one latest Conference Direction receive the
   PRISM-shared card. Users without the role are unchanged.
8. Shared-view search can match the system note by title, PRISM/Command/Conference
   terms, PO name, item name or direction text. Label/type filters intentionally
   exclude the virtual note because it has no Notebook labels/type.

IMPLEMENTATION NOTES
--------------------
- No database or EF migration.
- No Conference query/model changes.
- Existing IOfficerConferenceReadService remains the authoritative source.
- IndexModel augments only the rendered Shared-with-me count by one when the live
  digest exists.
- Notebook client count refreshes preserve that one-system-surface offset so an
  AJAX note mutation does not incorrectly reset Shared with me from 1 to 0.
- Empty-state reconciliation now recognises system-shared virtual cards.

FILES
-----
See CHANGED-FILES.txt.

APPLY
-----
Copy the package contents over the ProjectManagement project root.

Because notebook-app.js and notebook-board.js changed, the Notebook bundle must
be regenerated. Your ProjectManagement.csproj already runs the Notebook asset
build before Build/Publish when JS inputs are newer, but node_modules/esbuild
must exist first.

Recommended:
    npm ci
    npm run build:notebook
    npm run check:notebook-assets
    dotnet build
    dotnet test

Then hard-refresh Notebook (Ctrl+F5).

VALIDATION PERFORMED HERE
-------------------------
- Modified module JS parses successfully as ES modules.
- 82 dependency-free Notebook JavaScript tests passed, including 12 focused tests
  covering this Conference digest/shared-surface behaviour.
- CSS/Razor/C# structural brace counts are balanced.
- npm ci could not complete in this environment because the configured package
  registry returned 404 for xmlchars@2.2.0; therefore the esbuild bundle was not
  regenerated here.
- .NET SDK is not installed in this execution environment, so dotnet build/test
  must be run in the development environment.
