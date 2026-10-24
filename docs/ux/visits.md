# Visits to SDD

The Visits module records the dignitaries, courses and delegations hosted by the Simulator
Development Division: when they came, who they were, how many, what was discussed, and the
photos. Everyone signed in can read it; Admin, HoD and Project Office record and edit visits
(`ProjectOfficeReportsPolicies.ManageVisits`); only Admin manages visit types.

## Where it lives

| Concern | Code |
| --- | --- |
| Queries, create/update/delete, remarks preview, list thumbnail | `Areas/ProjectOfficeReports/Application/VisitService.cs` |
| Photos (upload, cover, derivatives, serving) | `VisitPhotoService.cs`, `Pages/Visits/ViewPhoto.cshtml.cs` |
| Excel and PDF exports | `VisitExportService.cs`, `Utilities/Reporting/Visit*Builder.cs` |
| Register (the module's main page) | `Pages/Visits/Index.cshtml(.cs)`; `/Visits/All` redirects here with its filters |
| Record page, forms | `Pages/Visits/Details`, `New`, `Edit`; fields `_VisitFields` + `VisitFormInput`, photo chooser `_PhotoPicker`, viewer `_PhotoViewer`, gallery `_PhotosPartial` |
| Styles | `wwwroot/css/project-office-reports/visits-register.css` (register, type tags), `visits-details.css` (record, gallery, viewer), `visits-editor.css` (forms); site tokens only |
| Script | `wwwroot/js/pages/project-office-reports/visits.js` (charts, viewer, row clicks, upload checks) |

## The register (`/ProjectOfficeReports/Visits`)

1. **Header**: title, one line on what the register is, and the actions: **Visit types** (Admin),
   **Export** (a menu: Excel workbook or PDF report with photos, both for the visits matching the
   current filters, and the menu says which), **Record visit** (managers).
2. **Headline figures**: last 12 months, last 30 days (visits and visitors hosted), the most recent
   visit and the number on record. They always cover the **whole register**, whatever the filters,
   so the figures at the top never change meaning. Both windows include today.
3. **Filters** inline (search, type, from, to). Search matches the visitor, the remarks and the type
   name. Inactive types stay in the type list, marked "(inactive)", because older visits carry them.
   Each active filter is a chip that removes just that filter; **Clear all** removes them all.
4. **Register**: the count and visitors for the filters, sort (newest, oldest, largest group,
   visitor A–Z, type) and rows per page (25, 50, 100). In date order the rows are grouped under
   month headings that show the month's totals across every matching visit, not only this page.
   - A row shows the date block (day, month and year, weekday), the cover photo (or the first photo
     when no cover is chosen) with the photo count, the visitor, the opening of the remarks, the
     type and the number of visitors. The remarks preview drops the visitor's name when the remarks
     start with it, since the name is on the line above.
   - The visitor name is the link to the visit; a click anywhere else on the row opens it too
     (Ctrl/Cmd-click opens a new tab). Managers get **Edit**.
   - Each type has a colour slot (1–6, by type name) used by both its tag and the type chart.
   - On phones each visit is a card: date block and visitor, the photo across the card, then type,
     visitors and Edit. Nothing scrolls sideways.
5. **Trends** follow the filters and say so: visits (bars) and visitors (line, straight segments)
   for the last 12 calendar months, and the mix of types for the last 12 months or all time. Chart
   colours come from the `--pm-chart-*` tokens and redraw when the theme changes; downloads keep
   the card background.

## The record page (`/Visits/Details/{id}`)

- Breadcrumb back to the register, then a plain header: the type tag (same colour as on the
  register), the visitor as the title, the date, the number of visitors and photos, and **Edit
  visit** / **Delete** for managers. Delete asks first and removes the photos too.
- The cover photo (or the first photo when none is chosen) shown large; it opens the photo viewer
  at that photo. **All N photos** jumps to the gallery.
- **Remarks** as typed, paragraphs and line breaks kept.
- **Photos**: the gallery, opening the same in-page viewer (`_PhotoViewer`). Managers get a link
  to the photo manager on the edit page.
- Beside them (below on narrow screens): **The visit** (date, type, visitors, photos; a type no
  longer used for new visits says so) and **Record**: who recorded it and when, and who last
  edited it and when, in IST. An edit within two minutes of recording does not count as an edit.
- A missing visit shows "Visit not found" with a way back, under a 404 status.
- A photo or cover whose file cannot be served shows a quiet "unavailable" placeholder.

## Record a visit and Edit visit

- Both forms render the same fields from `VisitFormInput` through `_VisitFields.cshtml`, in the
  same order: date, type and number of visitors on one row, then the visitor and the remarks
  (with a character counter against the 2,000 limit). Record starts from today's date in IST.
- Photos use `_PhotoPicker.cshtml`: previews of each chosen photo, its size, and a check against
  the limits (count, size per photo, total) that stops the form being sent while the selection
  breaks them. The server checks the same limits again.
- **Record visit** opens the new visit's record page; **Record and add another** returns to an
  empty form. Saving an edit returns to the record page. A rejected edit keeps what was typed.
- On Edit, the photo manager (`#visit-photo-manager`) uploads more photos and sets the cover or
  deletes a photo; each returns to the photo manager. Photos save on upload, separately from the
  details form. Captions are set per upload batch; there is no per-photo caption editor yet.
- The footer with the buttons stays in view on long forms, except on phones.

## Messages

Server messages use the shared layout toasts: `TempData["ToastSuccess"]` and `TempData["ToastError"]`.
Do not add a page-level toast pipeline. Browser-side checks (too many or too large photos) call
`showToast` in `visits.js`, which renders the same `app-toast` markup.

A failed export returns to the register with the filters kept and the reason as an error message.
