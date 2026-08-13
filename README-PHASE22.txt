PRISM Publications — Phase 22
Compendium 2.0 Foundation: Authoring Workspace, Project Selection & Shared Configurations

PURPOSE
Phase 22 converts the canonical Simulators Compendium page from an automatic proliferation-catalogue exporter into a first-class publication-authoring workspace. It intentionally does NOT perform the major Compendium PDF visual redesign planned for later phases.

CORE RULE
The candidate portfolio now follows the same normal publication scope as Brochure: Active/Ongoing and Completed PRISM projects that are not deleted or archived. “Available for proliferation” is a filter and a live project fact; it is no longer an inclusion gate.

IMPLEMENTED
1. Four-stage Compendium workspace: Publication settings → Select projects → Review publication → Publication readiness.
2. Search, lifecycle, project-category, technical-category, proliferation-status and Selected-only filtering.
3. Select matching (bounded to 100 per action), individual selection, clear selection and persistent selected state across filters.
4. Publication order right rail with native drag-and-drop plus move-up/move-down controls.
5. Selection-aware authoritative server preflight: only the current selected project order generates findings and category structure.
6. Preview/Download generate only selected projects. Empty technical categories disappear. Category order follows first occurrence in user order; project order within each category is preserved.
7. Ongoing projects are supported safely in the existing PDF: status is shown, completion year is not treated as applicable, and proliferation cost is rendered/validated only where the project is marked available for proliferation.
8. Multiple shared Saved Compendiums. All authorised users may load/use them; HoD/Comdt may create, update, rename, duplicate and soft-delete.
9. Saved configurations store publication identity, handling marking and ordered project membership only. Factual project data is always rehydrated live from PRISM.
10. Saved-configuration writes use optimistic row-version concurrency, transactional project-order replacement and the existing PRISM audit service.
11. Proper PRISM modal for “Discard and load” when switching saved Compendiums with unsaved working changes; no browser confirm() is used for this workflow.
12. Preflight has revision/abort protection: stale or pending server results can never re-enable Preview/Download after the working configuration changes.
13. Publications landing page copy is rewritten for users rather than developers/implementers.
14. Legacy automatic proliferation Compendium semantics are retained separately through GetProliferationCompendiumAsync for old bookmarks/integration compatibility.
15. Existing robust photo-derivative fallback chain is retained.

SAVED CONFIGURATION GOVERNANCE
- Load/use: every authorised Publications user.
- Maintain shared configuration: HoD or Comdt.
- Stored: title, subtitle, edition, classification marking, selected project IDs and order.
- Never stored: project description, Arm/Service, lifecycle, completion year, proliferation cost or other factual project data.
- Deleted/unavailable projects remain diagnosable through the saved project-name snapshot; current project facts are always live.

DATABASE
Additive migration: 20261208130000_AddSharedCompendiumPresets
Creates CompendiumPresets and CompendiumPresetProjects.
- Compendium preset names are unique while active.
- Project order is relational and protected by a unique (PresetId, SortOrder) constraint.
- Project membership is unique per preset.
- ProjectId is nullable with ON DELETE SET NULL so stale membership can be diagnosed rather than silently lost.
- User ownership/audit foreign keys are RESTRICT; preset deletion cascades to its membership rows.

PDF SCOPE IN THIS PHASE
The existing Compendium visual language is deliberately retained. Phase 22 only makes it publication-selection aware and safe for Ongoing/non-proliferation projects:
- cover/index/project sections contain selected projects only;
- the cover metric is “Projects”, not “Simulators” as an eligibility assumption;
- index status/year is lifecycle aware;
- proliferation-cost metadata is omitted where it is not applicable.

DEFERRED BY DESIGN
Phase 23: in-context authoritative correction, publication-specific image selection/crop, richer severity policy.
Phase 24: cover/index/category/project-page redesign, page planner and physical composition verification.
Phase 25: large-catalogue performance, regression hardening and final design freeze.

INSTALLATION
1. Apply this package on top of the current Phase 21.2 Brochure baseline.
2. Replace/add files exactly as listed in REPLACEMENT-MANIFEST.txt.
3. Apply the additive EF migration using the project's normal migration/deployment path.
4. Run the Phase 22 validator below before starting the application.

LOCAL VALIDATION
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase22.ps1

The validator performs JavaScript syntax/contracts and, when the .NET SDK is available, dotnet build and dotnet test.
