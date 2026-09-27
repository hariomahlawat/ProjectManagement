# Transfer of Technology tracker – workspace regression

> The older card/list toggle is no longer rendered. `IndexModel.ViewMode` (`Cards`/`List`) still binds and is carried through the filter modal and post-redirects, but `Tot/Index.cshtml` always shows the list/detail workspace described here. The file name is kept so existing links still work.

Use this checklist to confirm that the ToT tracker (`/ProjectOfficeReports/Tot`) renders its list and detail panels, preserves filters while you navigate, and shows the right actions for each role.

## Prerequisites

- Tracker access is open to any authenticated user (`ViewTotTracker`). To submit you need Admin, HoD, Project Office or Project Officer (`ManageTotTracker`). To approve you need Admin or HoD (`ApproveTotTracker`).
- Sample data: **completed**, non-archived, non-Repeat-Build projects covering the ToT statuses Not required, Not started, In progress and Completed, with at least one pending request. Repeat Build (`IsBuild`) and non-completed projects never appear in the tracker (`ProjectTotApplicabilityPolicy.EligibleProjectPredicate`).

## Steps

1. Go to **Project Office reports ▸ Transfer of Technology tracker**. Check the toolbar: **Export** (disabled when no rows match), **Summary view** and **More filters**.
2. The context bar shows the result count, a filter summary and **Pending approvals: N**.
3. The left panel lists projects with the columns Project, ToT status, Started, Completed, MET and Request. If you can approve, pending rows also show "Needs decision".
4. Click a row. The URL gains `SelectedProjectId`, the row is highlighted (`is-selected`), and the right panel shows the milestones (started, completed, MET details, MET completed, first production model).
5. Use the inline search, or open **Filters**/**More filters**. Set **ToT status** (for example *In progress*), a request state, *Only pending*, *Requires ToT only* or *MET completed only*, then apply. The filters and the selected project stay in the query string. Row links keep the active filters.
6. As a submitter who is not an approver, click **Submit update**. Submit with an optional remark. After the redirect the filters and selection are kept, and a success toast appears. A second submission while one is pending is blocked ("An update is already pending approval…").
7. As Admin or HoD, select a project with a pending request. Use the **HoD decision** card to approve or reject, with optional context. Check the redirect and that the request state updates.
8. Click **Latest ToT request** (shown when a request exists) to open the request modal.
9. Click **Export** and then **Export workbook**. The workbook reflects the active filters.

## Regression

- Keyboard: every row link, the modal triggers and the approve/reject buttons can be reached and operated.
- Narrow viewport (≤ 768 px): the list and detail panels stack without horizontal overflow.
- Known issue: when a user whose only remark role is Project Office submits or decides with a remark, the ToT change is saved but the toast reports that the remark could not be saved. `IndexModel.ResolveRemarkType` picks `External` for that role, and `RemarkService` rejects it.
