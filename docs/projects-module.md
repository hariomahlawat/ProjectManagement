# Projects Module

The projects feature brings together procurement data, execution timelines and collaboration tools for each project record.

## Overview workspace
* **Page:** `Pages/Projects/Overview.cshtml` and its backing model aggregate everything needed to understand a project at a glance.
* The page loads the project, category trail, stage ledger, plan editor state and assignment options in one request through `OverviewModel`.
* Procurement summaries are supplied by `ProjectProcurementReadService`, while `ProjectTimelineReadService` returns the current stage board, outstanding backfill flags and latest approval metadata.
* The offcanvas editor for procurement applies the same data, enabling instant validation errors to be surfaced back on the overview page through `TempData` keys.

## Procurement tracking
* Procurement inputs map to `ProjectFactBase` entities that capture the latest IPA, AON, benchmark, L1, PNC and supply-order details with creator metadata and concurrency tokens.
* `ProjectFactsService` performs all writes. Each upsert logs to the audit service, clears stage backfill flags where required and ensures records are created atomically per fact type.
* The procurement edit form (`Pages/Projects/Procurement/Edit.cshtml.cs`) enforces stage gating before persisting numbers or dates, rolls back on failure and prevents future-dated supply orders.

## Timeline management
* `ProjectTimelineReadService` projects a complete timeline view model from `ProjectStages`, combining canonical stage codes (`StageCodes.All`) with per-stage metadata.
* The same service highlights pending plan approvals and whether any stage still requires backfilling, feeding callouts on the overview screen. Per-user draft state (e.g. whether the current user owns a draft or submission) is sourced from `PlanReadService`.
* Detailed plan editing uses `PlanReadService` to hydrate both exact date and duration editors. Drafts are scoped to the current user (`OwnerUserId`) so Project Officers and HoDs can work privately in parallel while the service filters out other users’ drafts. `PlanCompareService` produces diffs for the pending submission shown in the HoD review panel.

## Role assignment
* The overview model builds an `AssignRolesVm` populated from the `HoD` and `Project Officer` role memberships, letting administrators record the responsible officers per project without leaving the page.
* Row versions from `Project` are preserved in the view model so concurrent edits can be detected during updates.

## Project conversations
* Rich comments are backed by `ProjectCommentService`, which supports project-wide or stage-specific threads, nested replies and soft deletion with audit logging.
* Attachments are filtered by allow-listed MIME types, capped at 25&nbsp;MB per file and stored under a configurable upload root (`PM_UPLOAD_ROOT`) with sanitised filenames.
* Comment forms (`Pages/Projects/CommentViewModels.cs`) provide typed categories (Update, Risk, Blocker, Decision and Info), optional stage targeting, pinning and multi-file uploads for collaboration history.
