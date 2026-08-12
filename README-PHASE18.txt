PRISM Capability Brochure — Phase 18
Shared Saved Brochures + Compact Publication Header
=====================================================

Purpose
-------
Phase 18 turns the Brochure Builder into a reusable institutional publication workspace.
HoD and Comdt can maintain multiple shared brochure configurations; every authorised user
can load one and use it as a working brochure. The phase also removes the redundant hero
chrome and the Offline PDF badge so the publication workspace starts higher on screen.

What is implemented
-------------------
1. Compact workspace header
   - Removes the CAPABILITY PUBLICATION eyebrow.
   - Removes the Offline PDF badge.
   - Shortens the permanent page description.
   - Uses the header-right workspace for Saved brochure selection and management.
   - Retains breadcrumb and Overview/Brochure/Compendium navigation.

2. Multiple shared Saved Brochures
   - Any authenticated/authorised Brochure user can list and load shared brochures.
   - HoD and Comdt alone can create, update, rename, duplicate and retire shared brochures.
   - Admin is not implicitly granted editorial write authority.
   - Ad-hoc brochure use remains fully supported; loading a preset is optional.

3. Durable configuration, not frozen content
   A shared brochure stores publication configuration only:
   - title/subtitle/edition/strapline metadata;
   - narrative source;
   - publication profile;
   - cover style and approved Cover A artwork choice;
   - advanced/institutional publication settings;
   - exact selected-project order;
   - per-project image treatment, primary/secondary image IDs and focal points;
   - Cover B hero choice/focal point.

   It deliberately does NOT store:
   - publication approvals or review fingerprints;
   - cover approval;
   - preflight findings or page plan;
   - PDF-verification state;
   - copied/frozen Project Brief text or photo bytes.

4. Live PRISM rehydration
   - Loading always resolves current authoritative project records and current photos.
   - Unavailable projects are skipped with an explicit load diagnostic.
   - Removed saved photos fall back safely to current Automatic image resolution and report a diagnostic.
   - Stale Cover B hero selections are reset safely.
   - Approvals are reset on load and current preflight is recalculated.

5. Shared-edit safety
   - Active configuration has a client dirty baseline.
   - HoD/Comdt see Modified; ordinary users see Modified locally.
   - Loading another saved brochure protects unsaved working changes.
   - Shared updates use an explicit optimistic concurrency token.
   - A stale editor receives HTTP 409 and is offered Reload current version or Save my version as new.
   - Delete is a soft retirement; existing generated PDFs are unaffected.
   - Audit metadata records creator/updater and timestamps.

6. Management workflow
   - Load from header selector.
   - Save changes to the active shared brochure.
   - Save current working brochure as new.
   - Rename.
   - Duplicate the stored version.
   - Manage all shared brochures in a compact modal.
   - Retire/delete with confirmation.

Database
--------
This phase adds:
- BrochurePresets
- BrochurePresetProjects

Migration:
  20261208100000_AddSharedBrochurePresets

The migration is additive. The project already maintains its migration manifest, and the new
migration ID is included in Migrations/immutable-migration-ids.txt.

Important deployment note
-------------------------
Phase 18 is designed to be applied on top of the Phase 17 code you already tested.
Replace every file listed in REPLACEMENT-MANIFEST.txt, then build/test normally. Apply the
new EF Core migration through the application's normal deployment/startup migration process.

Permissions
-----------
Read/load: all users who can access the Brochure Builder.
Write/manage: HoD or Comdt only, enforced server-side in both the Razor Page and preset service.
Client-side visibility is convenience only and is not used as the security boundary.

Validation performed in the delivery environment
-------------------------------------------------
PASS  node --check wwwroot/js/pages/projects-brochure.js
PASS  node --test wwwroot/js/projects/publications-brochure-contract.test.js
      69 tests passed; 0 failed.
PASS  structural delimiter checks across all modified/new C# files and the EF snapshot.
PASS  migration manifest contains the Phase 18 migration ID exactly once.
PASS  Brochure Razor view no longer contains the Offline PDF badge markup.

The delivery environment does not contain the .NET SDK, therefore dotnet build/test could
not be executed here. Run tools/Test-PrismPublicationsPhase18.ps1 on the development machine.

Expected acceptance scenario
----------------------------
1. Sign in as HoD/Comdt.
2. Build a brochure, choose projects/order/settings and Save as new.
3. Confirm it appears in Saved brochure and Manage saved brochures.
4. Sign in as a normal authorised user and load the same saved brochure.
5. Confirm settings, exact project order and publication image choices are restored.
6. Confirm project approvals are required again and current PRISM content is used.
7. Modify the working copy as a normal user and confirm Modified locally appears without any Save management action.
8. As HoD/Comdt, update the shared brochure and verify a concurrently stale editor receives a conflict rather than silently overwriting it.
9. Run preflight/preview and confirm Phase 17 page planning/effective-DPI/PDF verification behaviour remains unchanged.
