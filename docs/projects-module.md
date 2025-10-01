# Projects module storyboard

The projects module weaves procurement data, execution controls, plan collaboration and conversation history into a single guided workspace so delivery teams can steer each project from inception through close-out. This document expands the existing technical notes into a storyboard-style walkthrough that describes what happens at every stage, who performs the action, and which supporting services keep the experience consistent.

## Personas and permissions

| Persona | Typical responsibilities | How access is enforced |
| --- | --- | --- |
| **Project creators / portfolio admins** | Stand up new projects, pick categories, seed historic stage completions, and assign the initial leadership team. | Only users satisfying the `Project.Create` policy can reach the create page, and it validates category, case file and stage details before persisting to the database and audit log.【F:Pages/Projects/Create.cshtml.cs†L20-L190】 |
| **Heads of Department (HoDs)** | Own the authoritative timeline, review or directly apply stage changes, approve plans and act on escalations. | HoD-only endpoints power both the decision flow (`[Authorize(Roles = "HoD")]`) and the off-canvas direct apply modal. The services always check the stored HoD assignment case-insensitively before applying updates.【F:Pages/Projects/Stages/DecideChange.cshtml.cs†L18-L115】【F:Services/Stages/StageDecisionService.cs†L18-L118】【F:Services/Stages/StageDirectApplyService.cs†L41-L121】 |
| **Project Officers (POs)** | Maintain procurement facts, prepare plan drafts, request stage transitions, and add project commentary. | Stage-change requests are limited to the project’s assigned PO, drafts are scoped to the logged-in user, and procurement edits respect stage completion gates.【F:Services/Stages/StageRequestService.cs†L32-L152】【F:Services/Stages/PlanReadService.cs†L41-L212】【F:Services/Projects/ProjectFactsService.cs†L27-L200】 |
| **Administrators** | Adjust HoD/PO assignments, review audit trails, and unblock concurrency conflicts. | The assign-roles surface is limited to Admins and HoDs, preserves the project row version, and writes to the audit trail after every successful change.【F:Pages/Projects/AssignRoles.cshtml.cs†L16-L116】 |

All authenticated users can enter the project overview to gather context, but write operations are tightly bound to these persona-specific checks so that each step in the storyboard is owned by the right role.【F:Pages/Projects/Overview.cshtml.cs†L24-L140】

## Lifecycle at a glance

1. **Initiate and seed the project**  
   The creator completes the create form with name, case file number, description, optional historic stage completion and initial HoD/PO assignments. The handler enforces unique case file numbers, prevents future completion dates, automatically back-fills an initial `ProjectStage` if the project is marked as ongoing, and records the event in the audit log before redirecting to the overview workspace.【F:Pages/Projects/Create.cshtml.cs†L77-L190】

2. **Arrive on the overview workspace**  
   Every persona lands on the Overview page, which hydrates the core `Project`, the category breadcrumb, the ordered stage ledger, procurement highlights, timeline snapshot, plan editor state and assignment options in a single round trip.【F:Pages/Projects/Overview.cshtml.cs†L59-L140】  Diagnostic hashes in the logs help operations teams tie incidents to the correct environment without exposing secrets.【F:Pages/Projects/Overview.cshtml.cs†L75-L138】

3. **Clarify leadership and responsibilities**  
   From the overview off-canvas, Admins and HoDs can adjust HoD/PO assignments. The request preserves the project row version to surface concurrency errors, reloads the selection lists with descriptive names, and logs before/after identifiers for traceability.【F:Pages/Projects/AssignRoles.cshtml.cs†L31-L116】  The overview view model mirrors those options so everyone can see who currently owns the project.【F:Pages/Projects/Overview.cshtml.cs†L129-L181】

4. **Track procurement milestones**  
   The procurement summary tiles expose the latest IPA, AON, Benchmark, L1, PNC and supply-order values. When a Project Officer edits a fact, `ProjectFactsService` performs the atomic upsert, clears any associated backfill flags for the gating stage, and records an audit event that identifies the project, amount and fact type.【F:Services/Projects/ProjectFactsService.cs†L27-L200】  Each fact type is guarded by stage completion rules so data can only be captured once the relevant stage has progressed, and overview immediately reflects the update because the same read model powers both the page and the off-canvas editor.【F:Pages/Projects/Overview.cshtml.cs†L99-L127】

5. **Plan collaboratively, without collisions**  
   `PlanReadService` prepares both an exact-date editor and a duration-based planner, wiring in holiday policies and anchoring rules. Drafts are scoped to the current user (`OwnerUserId`), and when another user already has a pending submission the state model locks the editor and explains why further submissions are blocked. Approval history, rejection notes and last-saved timestamps are surfaced alongside the draft so POs and HoDs can pick up where they left off.【F:Services/Stages/PlanReadService.cs†L41-L212】  The overview page exposes whether backfill is required and whether a plan is pending approval, guiding HoDs toward the review workflow when needed.【F:Pages/Projects/Overview.cshtml.cs†L101-L138】

6. **Request, review and apply stage changes**  
   *Project Officer request:* The PO initiates a change through `StageRequestService`, which validates stage codes, checks the requester against the assigned PO, runs the shared validator to enforce predecessor and date rules, and captures both the request and an audit log entry describing the status transition. Duplicate pending requests are blocked to keep the HoD queue clear.【F:Services/Stages/StageRequestService.cs†L32-L151】  
   *HoD decision:* The HoD reviews pending items via `StageDecisionService`. Decisions are logged with connection hashes, reject paths retain the existing status, and approvals enforce transition policies before updating `ProjectStage`, clamping inconsistent dates and writing approval logs.【F:Services/Stages/StageDecisionService.cs†L48-L200】  
   *HoD direct apply:* When time-critical, the HoD can bypass the queue using `StageDirectApplyService`. The service materialises any missing stages, reuses validation, optionally backfills predecessors (emitting auto-backfill logs), and supports administrative completions without dates by marking the stage as incomplete data. Superseded PO requests are annotated, and warnings bubble back to the UI so the HoD understands any automatic adjustments.【F:Services/Stages/StageDirectApplyService.cs†L41-L200】

7. **Monitor execution health**  
   The timeline view layers status chips, plan approvals and health signals. `StageHealthCalculator` computes slip for every stage and assigns an overall RAG value: seven or more days late marks Red, one to six days late (or tasks due within two days) elevates to Amber, while completed or skipped stages are excluded from the proactive Amber threshold.【F:Models/Execution/StageHealthCalculator.cs†L19-L96】  The overview’s `TimelineVm` also exposes outstanding backfill flags so teams know which stages still need procurement facts or date corrections before progressing.【F:Pages/Projects/Overview.cshtml.cs†L101-L138】

8. **Capture conversations and decisions**
   `ProjectCommentService` underpins threaded discussions across the project or within a specific stage. It enforces reply-stage consistency, stores metadata about who created or edited each comment, and writes audit events for adds, edits and soft deletes. Attachments are limited to allow-listed MIME types, capped at 25 MB per file, saved under a configurable root (`PM_UPLOAD_ROOT`), and sanitised before persisting. Only the original author can edit or delete their comment, preserving accountability.【F:Services/ProjectCommentService.cs†L22-L147】【F:Services/ProjectCommentService.cs†L174-L238】  Comment forms expose standardised categories (Update, Risk, Blocker, Decision, Info) and optional pinning so meeting notes and escalations are easy to find.

9. **Project photos and gallery**
  Admins, assigned HoDs and the project’s Project Officer can open the photo workspace; the page double-checks the caller’s role assignments before allowing gallery changes and falls back to a 403 when the user is not authorised.【F:Pages/Projects/Photos/Index.cshtml.cs†L18-L121】  Uploads run through `ProjectPhotoService`, which sanitises file names, enforces size and dimension rules, supports optional cropping, and generates derivative images beneath a deterministic `projects/{projectId}/{storageKey}-{size}.(webp|png)` folder structure.【F:Services/Projects/ProjectPhotoService.cs†L82-L207】【F:Services/Projects/ProjectPhotoService.cs†L244-L420】【F:Services/Projects/ProjectPhotoService.cs†L501-L536】  Setting a new cover photo automatically clears previous covers and updates the project’s `CoverPhotoVersion`, while deletes remove derivative files and promote the next available photo (falling back to clearing the cover when none remain); every add/update/remove operation emits an audit event for traceability.【F:Services/Projects/ProjectPhotoService.cs†L210-L360】  The overview page reloads the ordered gallery and cover metadata on every request, so changes made in the photo workspace are immediately reflected for all viewers.【F:Pages/Projects/Overview.cshtml.cs†L94-L138】

#### Accessibility manual QA — photo reorder

1. Open **Projects → Photos → Reorder** for a project with at least two uploaded images.
2. Tab to a photo card, press Space to grab it, then use the arrow keys to move it; confirm the live region announces the selection and each move.
3. Press Escape while a card is grabbed to cancel the move and hear the cancellation announcement.
4. Complete a drag-and-drop reorder with the mouse and verify the final position is announced and focus returns to the moved card.

10. **Audit and iterate**
  Almost every mutating action—project creation, role assignment, procurement updates, stage requests, stage decisions and comment changes—records an audit event with contextual metadata. These trails allow administrators to reconstruct the project narrative over time and update this documentation as new capabilities ship.

## Updating this storyboard

When new functionality lands, extend the relevant lifecycle step (or add a new one) with:

1. The persona responsible and how permissions are enforced.
2. The happy-path flow, including validations and side-effects.
3. Any warnings, concurrency controls or audit outputs that support operations.

This keeps the storyboard grounded in real behaviours while remaining future-proof as the module grows.
