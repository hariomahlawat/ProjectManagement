# Transfer of Technology tracker – view mode regression

Use this checklist to confirm that the Transfer of Technology (ToT) tracker page renders both layout modes and preserves filters between navigation steps.

## Prerequisites

- User with Project Office Reports permissions that can open the ToT tracker.
- Sample projects covering different ToT statuses and approval states (pending, approved, rejected) with at least one remark to show contextual data.

## Steps

1. Navigate to **Project Office reports ▸ Transfer of Technology tracker**.
2. Verify that the cards view is selected by default and that each project renders as a `.tot-project-card` tile with the expected ToT and request badges.
3. Click **List view** in the segmented toggle.
   - Confirm that the query string adds `ViewMode=List` and the filter summary remains intact.
   - Ensure each project row switches to the compact table layout with columns for project info, status badges, latest remark snippet, and actions.
   - Check that the current selection (if any) keeps its highlight border.
4. Activate a filter (for example, set **ToT status** to *In progress*) and apply it.
   - Observe that the chosen view mode persists after the page reloads.
5. Select a project via **View details**. The selection indicator should remain in the active layout and the project details panel should update without switching view modes.
6. Switch back to **Card view** and confirm that the previously applied filters and selected project persist.
7. For pending requests, submit a decision or update via the modals and ensure the postback redirects keep the chosen view mode in the URL.

## Regression

- Re-run accessibility checks for the toggles (keyboard focus, aria labels).
- Validate the layout on a narrow viewport (≤ 768px) to ensure the list rows stack correctly.
- Capture updated screenshots for documentation when either view changes materially.
