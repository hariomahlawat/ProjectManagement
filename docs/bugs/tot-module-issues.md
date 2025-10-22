# ToT Module Critical Bug Fix Tasks

This document tracks the tasks required to address the three critical issues identified during the ToT module review. Each task lists the implementation work, validation requirements, and references to keep in mind for code quality and regression prevention.

## 1. Allow the tracker to degrade gracefully when ToT request metadata columns are unavailable

**Goal:** Ensure `ProjectTotTrackerReadService` can recover from column-mismatch errors by avoiding optional request metadata when the fallback query path is used.

**Implementation Tasks**
- Update `ProjectTotTrackerReadService.BuildProjectSnapshotQuery` so the fallback projection omits `TotRequest.RowVersion` when `includeRequestDetailColumns` is `false`, returning a nullable slot instead of referencing the missing column.
- Audit callers (notably `Areas/Projects/Pages/Tot/Index.cshtml.cs`) to handle a `null` `RequestRowVersion` by hiding approval-only UI and surfacing a non-blocking status message if the metadata is unavailable.
- Add or update integration/unit coverage in `ProjectTotTrackerReadServiceTests` that simulates an `UndefinedColumn` error from the initial query, verifies the fallback path succeeds, and asserts that existing columns continue to load as expected.

**Quality Considerations**
- Preserve existing query performance by reusing compiled SQL where possible.
- Ensure null-handling paths are covered by tests to avoid regressions in the Razor page model.

## 2. Handle zero-length ToT request row versions during approval

**Goal:** Prevent the approval workflow from rejecting requests whose stored row version value is present but empty.

**Implementation Tasks**
- Normalize zero-length `RequestRowVersion` byte arrays to `null` within `Index.cshtml.cs` before converting to Base64 so that unusable concurrency tokens are omitted from the form payload.
- Adjust the decide handler to distinguish between “no request selected” and “request with missing concurrency token,” allowing legitimate approvals to proceed when a proper token is available.
- Extend the page model tests to cover approvals on records with empty row versions and confirm the workflow no longer returns the false validation error.

**Quality Considerations**
- Maintain defensive checks to avoid double-post approvals or concurrency conflicts.
- Use descriptive validation messages so stakeholders understand why a request cannot be approved when the token is absent.

## 3. Enforce chronological validation for ToT milestones

**Goal:** Ensure milestone dates follow a chronological order relative to the ToT start and completion dates.

**Implementation Tasks**
- Update `ProjectTotService.ValidateRequest` to reject MET completion or first-production dates that fall before the ToT start date, and to reject MET/first-production dates that fall after the ToT completion date when it is provided.
- Provide targeted validation messages explaining the chronological rule so users can correct their submissions quickly.
- Add unit tests in `ProjectTotServiceTests` that cover MET/first-production dates occurring before the start or after the completion date and confirm the validation errors are triggered.

**Quality Considerations**
- Keep validation logic centralized to maintain consistency between API and UI workflows.
- Localize new validation messages and ensure they follow existing message formatting conventions.

