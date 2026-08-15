# PRISM Publications Phase 31 — Adaptive Project Dossier Composer

## Purpose
Phase 31 redesigns the **inside project dossier page** around the information now required for the Compendium. It replaces the old equal-weight ERP facts treatment with a modular publication composition that adapts to project content instead of leaving empty boxes or shrinking text aggressively.

The project dossier now prioritises:
- Project Name;
- one to three publication photographs with the Project Brief/narrative;
- Sponsoring Line Directorate;
- Proliferation Cost;
- IPR credentials only when **Filed** or **Granted**;
- Technology Transfer only when **In Progress** or **Completed**, with completion year only when completed;
- 1–6 authoritative Hardware / Technical Specification bullets.

Test-data quality (stock/watermarked/unrelated images and placeholder text) is intentionally outside the scope of this phase. Existing readiness safeguards remain, but Phase 31 focuses on layout, modularity and publication presentation.

## Installation
1. Back up or commit the current ProjectManagement source.
2. Confirm the corrected **Phase 30.1** source is already applied.
3. Extract `PRISM_Publications_Phase31_ReadyToPaste.zip` into the **ProjectManagement project root** and overwrite matching files.
4. Apply the EF Core migration:

```powershell
 dotnet ef database update
```

The new migration is:

`20261208190000_AddCompendiumAdaptiveDossiers`

5. Run validation:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase31.ps1
```

## 1. Four adaptive dossier families
Every Compendium project now supports these controlled layout families:

- **Automatic** — recommended; PRISM chooses from current project content.
- **Visual Hero** — strong single-image, lower content pressure.
- **Balanced Dossier** — default general-purpose image + narrative composition.
- **Multi-image Editorial** — large primary image plus one or two supporting images.
- **Technical / Content-heavy** — photography yields space to long brief/specification content.

Automatic resolution is deterministic. It evaluates title length, narrative pressure, programme-information modules, available publication photographs and the rendered pressure of technical-specification bullets. No AI is required to choose a page layout.

A publisher can override the layout per project from Review and can reset it to Automatic.

## 2. Modular dossier page grammar
The active PDF project page has been redesigned around:

1. publication running header / custom section;
2. **Project Name**;
3. adaptive **Photography + Project Brief/narrative** composition;
4. dynamic **Programme Information**;
5. optional **Hardware / Technical Specification**;
6. institutional footer.

The previous lifecycle/category/technical-category/indicative-cost facts grid is no longer the visual centre of the dossier. Category and lifecycle facts remain authoritative in PRISM and remain available to filtering/grouping/index logic, but the individual project dossier is now capability-led rather than ERP-record-led.

## 3. Programme Information
The programme-information band renders only facts actually present. It can contain:

- **Sponsoring Line Directorate** — resolved from the authoritative Line Directorate relationship, with the legacy Arm/Service value retained as a compatibility fallback;
- **Proliferation Cost**;
- **IPR** — Patent/Copyright credentials only for Filed or Granted records;
- **Technology Transfer** — In Progress or Completed; completion year is shown only for Completed ToT.

No empty publication boxes are generated for absent optional information. Remaining modules automatically redistribute across the available width.

IPR is presented as a compact publication credential rather than ordinary metadata.

## 4. Hardware / Technical Specification — authoritative project data
Phase 31 adds first-class project master data for technical specifications:

`ProjectTechnicalSpecificationItem`

Each project may store **1–6 ordered bullets**. Each bullet supports up to **750 characters**, allowing both very short requirements and substantial technical statements.

The existing Project content workspace now includes a **Hardware / technical specification** tab with:
- add/remove;
- move up/down ordering;
- maximum-count validation;
- duplicate detection;
- project RowVersion concurrency protection;
- audit logging.

The Compendium does not maintain a second copy of these facts; it reads them live from the Project master record.

## 5. Adaptive specification rendering
Technical specifications do not use a fixed-height box.

The PDF/live proof chooses a compact composition from actual text pressure:
- short 1–2 item sets use minimal vertical space;
- moderate sets use two columns where readable;
- short 4–6 item sets may use three columns;
- long bullets remain in one or two wider columns;
- high-pressure content switches toward the Technical dossier family;
- exceptional content uses deterministic continuation pages rather than microscopic typography.

Photography yields space before body typography is reduced beyond publication-safe limits.

## 6. One to three dossier photographs
Each Compendium project now supports:

- **Primary image**;
- **Supporting image 1**;
- **Supporting image 2**.

Each slot independently retains:
- Automatic or explicit selection;
- Photo ID;
- Fit / Fill;
- focal point/crop.

Automatic supporting-image resolution avoids duplicate photo IDs wherever possible. Multi-image Editorial is available only when enough usable project photographs exist.

Review provides a **Manage page images** workflow with role-specific image selection and independent Fit/Fill/crop controls.

## 7. Shared layout intent across live proof and PDF
The browser proof and PDF now share the same dossier concepts:
- effective layout family;
- primary/supporting image roles;
- dynamic programme modules;
- technical-specification module;
- title pressure treatment;
- continuation semantics.

The PDF remains the final rendering authority, but Review now mirrors the modular page grammar closely enough for meaningful editorial approval.

## 8. Review integrity
Review fingerprints advance to `compendium-review-v5` and now include:
- dossier layout choice;
- all dossier image slots;
- Fit/Fill/focal presentation state;
- technical specifications;
- IPR credentials;
- Technology Transfer state.

A meaningful change to these facts or presentation decisions invalidates a previous project review, preventing stale approval from surviving a changed dossier.

## 9. Persistence and migration
Compendium preset schema advances to **v7**.

The migration adds publication-presentation fields to `CompendiumPresetProjects` and creates `ProjectTechnicalSpecificationItems`.

This keeps responsibilities clean:
- Project technical specifications = authoritative Project master data.
- Dossier layout/photo slots = Compendium publication presentation data.

Existing Phase 30 cover composition, Custom Sections, Structure Editor, Review and readiness persistence remain intact.

## 10. Continuation pages and index integrity
One project still targets one page for normal cases. When exceptional narrative/specification pressure cannot fit safely, PRISM creates a controlled continuation page headed **TECHNICAL REFERENCE** or the narrative continuation label.

The index continues to point to the project's first dossier page, while physical page planning accounts for all continuation pages before subsequent page numbers are assigned.

## Build identity

`CompendiumPdf_2026-08-14_adaptive-dossier-v10`

## Validation completed in the delivery environment
- `projects-compendium.js` syntax: PASS
- `projects-compendium-structure-editor.js` syntax: PASS
- `project-content.js` syntax: PASS
- Complete Compendium JavaScript contract suite including Phase 31: **103 / 103 PASS**
- `git diff --check`: PASS
- Changed C# structural delimiter sanity checks: PASS
- Ready-to-paste reconstruction is verified during final packaging.

The delivery environment does **not** contain the .NET SDK. No `dotnet build` or `dotnet test` pass is claimed here. `Test-PrismPublicationsPhase31.ps1` runs those automatically when the SDK is available on the development workstation.
